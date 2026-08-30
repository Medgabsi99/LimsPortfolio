using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;

namespace Lims.SoapService.Soap;

/// <summary>
/// Minimal, dependency-free SOAP 1.1 endpoint for ASP.NET Core.
/// - POST {path}          : dispatches the operation found in soap:Body
/// - GET  {path}?wsdl     : serves a WSDL contract for client proxy generation
/// - Errors               : standard SOAP Fault responses (HTTP 500, per spec)
/// This mimics a classic .asmx/WCF endpoint so legacy ERP/MES clients
/// (WCF, MSXML2, VB Script) can integrate with the LIMS without changes.
/// The business service is resolved per-request from RequestServices.
/// </summary>
public class SoapEndpointMiddleware
{
    private const string SoapNs = "http://schemas.xmlsoap.org/soap/envelope/";

    private readonly RequestDelegate _next;
    private readonly string _path;
    private readonly string _serviceNamespace;
    private readonly string? _apiKey;   // null = auth disabled (development only)

    public SoapEndpointMiddleware(RequestDelegate next, string path, string serviceNamespace,
        IConfiguration configuration)
    {
        _next = next;
        _path = path;
        _serviceNamespace = serviceNamespace;
        _apiKey = configuration["SoapService:ApiKey"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.Equals(_path, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (HttpMethods.IsGet(context.Request.Method))
        {
            if (context.Request.Query.ContainsKey("wsdl"))
            {
                // WSDL is intentionally public so client tools can generate proxies.
                await ServeWsdlAsync(context);
            }
            else
            {
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(
                    $"LIMS SOAP 1.1 endpoint. POST to submit operations; " +
                    $"GET {_path}?wsdl to retrieve the service contract.");
            }
            return;
        }

        // ── API key authentication ─────────────────────────────────────────
        // Require X-Api-Key header for all POST operations.
        // Configure SoapService:ApiKey in appsettings.json / environment.
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided) ||
                !string.Equals(provided, _apiKey, StringComparison.Ordinal))
            {
                await WriteSoapFaultAsync(context, "Client",
                    "Authentication required. Provide a valid X-Api-Key header.");
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                return;
            }
        }

        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            return;
        }

        await HandleSoapRequestAsync(context);
    }

    private async Task HandleSoapRequestAsync(HttpContext context)
    {
        try
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var bodyText = await reader.ReadToEndAsync();

            var envelope = XDocument.Parse(bodyText);
            var body = envelope.Root?.Element(XName.Get("Body", SoapNs))
                       ?? throw new SoapFaultException("Client", "Missing soap:Body element.");

            var operationElement = body.Elements().FirstOrDefault()
                       ?? throw new SoapFaultException("Client", "Empty soap:Body - no operation found.");

            var service = context.RequestServices.GetRequiredService<SampleSoapService>();
            var resultElement = await DispatchAsync(service, operationElement);

            await WriteSoapResponseAsync(context, BuildEnvelope(resultElement));
        }
        catch (SoapFaultException fault)
        {
            await WriteSoapFaultAsync(context, fault.FaultCode, fault.Message);
        }
        catch (Exception ex)
        {
            await WriteSoapFaultAsync(context, "Server", "Internal error: " + ex.Message);
        }
    }

    private static async Task<XElement> DispatchAsync(SampleSoapService service, XElement operation)
    {
        var name = operation.Name.LocalName;

        if (name.Equals("Ping", StringComparison.OrdinalIgnoreCase))
            return service.Ping();

        if (name.Equals("GetSampleStatus", StringComparison.OrdinalIgnoreCase))
            return await service.GetSampleStatus(operation);

        if (name.Equals("SubmitResult", StringComparison.OrdinalIgnoreCase))
            return await service.SubmitResult(operation);

        if (name.Equals("GetOverdueCalibrations", StringComparison.OrdinalIgnoreCase))
            return await service.GetOverdueCalibrations();

        throw new SoapFaultException("Client", $"Unknown operation '{name}'.");
    }

    private static XElement BuildEnvelope(XElement payload) =>
        new(XName.Get("Envelope", SoapNs),
            new XAttribute(XNamespace.Xmlns + "soap", SoapNs),
            new XElement(XName.Get("Body", SoapNs), payload));

    private static async Task WriteSoapResponseAsync(HttpContext context, XElement envelope)
    {
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "text/xml; charset=utf-8";
        await context.Response.WriteAsync(envelope.ToString(SaveOptions.DisableFormatting));
    }

    private static async Task WriteSoapFaultAsync(HttpContext context, string code, string message)
    {
        var fault = BuildEnvelope(new XElement(XName.Get("Fault", SoapNs),
            new XElement("faultcode", code),
            new XElement("faultstring", message)));

        // SOAP 1.1 spec: faults are returned with HTTP 500
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "text/xml; charset=utf-8";
        await context.Response.WriteAsync(fault.ToString());
    }

    /// <summary>Serves a minimal WSDL so tools can generate client proxies.</summary>
    private async Task ServeWsdlAsync(HttpContext context)
    {
        var wsNs = _serviceNamespace;
        var wsdl = new XElement(XName.Get("definitions", "http://schemas.xmlsoap.org/wsdl/"),
            new XAttribute("targetNamespace", wsNs),
            new XAttribute(XNamespace.Xmlns + "tns", wsNs),
            new XAttribute(XNamespace.Xmlns + "soap", "http://schemas.xmlsoap.org/wsdl/soap/"),
            new XElement(XName.Get("service", "http://schemas.xmlsoap.org/wsdl/"),
                new XAttribute("name", "LimsSampleService"),
                new XElement(XName.Get("port", "http://schemas.xmlsoap.org/wsdl/"),
                    new XAttribute("name", "LimsSampleServicePort"),
                    new XAttribute("binding", "tns:LimsSampleServiceBinding"),
                    new XElement(XName.Get("address", "http://schemas.xmlsoap.org/wsdl/soap/"),
                        new XAttribute("location", _path)))),
            new XElement(XName.Get("documentation", "http://schemas.xmlsoap.org/wsdl/"),
                "LIMS SOAP 1.1 interop endpoint. Operations: Ping, GetSampleStatus, " +
                "SubmitResult, GetOverdueCalibrations."));

        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "text/xml; charset=utf-8";
        await context.Response.WriteAsync(wsdl.ToString());
    }
}