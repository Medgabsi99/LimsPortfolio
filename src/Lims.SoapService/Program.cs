using Lims.Infrastructure;
using Lims.SoapService.Soap;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLimsInfrastructure(builder.Configuration);
builder.Services.AddScoped<SampleSoapService>();
builder.Services.AddHealthChecks();   // liveness probe for monitoring

var app = builder.Build();

// SOAP 1.1 endpoint at /LimsSampleService.asmx (classic interop path).
// WSDL available at /LimsSampleService.asmx?wsdl
app.UseMiddleware<SoapEndpointMiddleware>(
    "/LimsSampleService.asmx",
    "http://lims.local/soap/2026");

app.MapGet("/", () => Results.Text(
    "LIMS SOAP service is running. Endpoint: /LimsSampleService.asmx (WSDL: ?wsdl)"));

app.MapHealthChecks("/health");

app.Run();