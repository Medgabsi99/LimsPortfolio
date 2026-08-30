using System.Xml.Linq;
using Lims.Core.Interfaces;
using Lims.Core.Models;
using Lims.Core.Services;

namespace Lims.SoapService.Soap;

/// <summary>
/// Business operations exposed over SOAP 1.1.
/// Each operation receives the request XML element and returns the response
/// XML element (namespace-agnostic parsing for maximum client interop:
/// WCF, ASMX proxies, VB6/MSXML2, Python zeep, Postman...).
/// </summary>
public class SampleSoapService
{
    private readonly ISampleRepository _repository;
    private readonly ILogger<SampleSoapService> _logger;

    public SampleSoapService(ISampleRepository repository, ILogger<SampleSoapService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>Health check used by monitoring tools and load balancers.</summary>
    public XElement Ping() =>
        new("PingResponse", new XElement("Result", "PONG"));

    /// <summary>
    /// Namespace-agnostic child lookup: clients may or may not qualify the
    /// payload with a default xmlns, so we match on LocalName only.
    /// </summary>
    private static XElement? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

    private static string RequiredText(XElement parent, string localName) =>
        Child(parent, localName)?.Value
            ?? throw new SoapFaultException("Client", $"Missing '{localName}' element.");

    /// <summary>Returns the lifecycle status of a sample (ERP order tracking).</summary>
    public async Task<XElement> GetSampleStatus(XElement request)
    {
        var sampleCode = RequiredText(request, "sampleCode");

        var sample = await _repository.GetSampleByCodeAsync(sampleCode)
                     ?? throw new SoapFaultException("Sender", $"Sample '{sampleCode}' not found.");

        var completed = sample.Tests.Count(t => t.TestStatus == "COMPLETED");
        var failed = sample.Tests.Count(t => t.Passed == false);

        return new XElement("GetSampleStatusResponse",
            new XElement("sampleCode", sample.SampleCode),
            new XElement("status", sample.Status),
            new XElement("clientName", sample.ClientName),
            new XElement("collectedAt", sample.CollectedAt.ToString("yyyy-MM-dd HH:mm:ss")),
            new XElement("totalTests", sample.Tests.Count),
            new XElement("completedTests", completed),
            new XElement("failedResults", failed),
            sample.Tests.Select(t => new XElement("test",
                new XElement("testCode", t.TestCode),
                new XElement("testName", t.TestName),
                new XElement("status", t.TestStatus),
                new XElement("resultValue", t.ResultValue?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XElement("unit", t.Unit),
                new XElement("passed", t.Passed))));
    }

    /// <summary>Submits an analytical result from an external system (MES / instrument gateway).</summary>
    public async Task<XElement> SubmitResult(XElement request)
    {
        var submission = new ResultSubmission
        {
            SampleCode = Child(request, "sampleCode")?.Value ?? string.Empty,
            TestCode = (Child(request, "testCode")?.Value ?? string.Empty).ToUpperInvariant(),
            InstrumentCode = Child(request, "instrumentCode")?.Value,
            Comment = Child(request, "comment")?.Value,
            Source = "SOAP_SERVICE"
        };

        var valueText = RequiredText(request, "resultValue");

        if (!decimal.TryParse(valueText, System.Globalization.NumberStyles.Number,
                              System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new SoapFaultException("Client", $"Invalid decimal value '{valueText}'.");

        submission.ResultValue = value;

        var errors = DomainValidators.Validate(submission);
        if (errors.Count > 0)
            throw new SoapFaultException("Client", string.Join(" | ", errors));

        var result = await _repository.SubmitResultAsync(submission);
        _logger.LogInformation("SOAP result accepted for {SampleCode}/{TestCode}", submission.SampleCode, submission.TestCode);

        return new XElement("SubmitResultResponse",
            new XElement("passed", result.Passed),
            new XElement("sampleStatus", result.SampleStatus));
    }

    /// <summary>Lists instruments whose calibration is overdue (quality compliance).</summary>
    public async Task<XElement> GetOverdueCalibrations()
    {
        var instruments = await _repository.GetInstrumentsAsync();
        return new XElement("GetOverdueCalibrationsResponse",
            instruments.Where(i => i.IsOverdue).Select(i => new XElement("instrument",
                new XElement("instrumentCode", i.InstrumentCode),
                new XElement("instrumentName", i.InstrumentName),
                new XElement("lastCalibrationAt", i.LastCalibrationAt?.ToString("yyyy-MM-dd")),
                new XElement("nextCalibrationDue", i.NextCalibrationDue?.ToString("yyyy-MM-dd")))));
    }
}

/// <summary>Maps to a SOAP 1.1 Fault response.</summary>
public class SoapFaultException : Exception
{
    /// <summary>SOAP fault code: "Client" (4xx-equivalent) or "Server".</summary>
    public string FaultCode { get; }

    public SoapFaultException(string faultCode, string message) : base(message)
        => FaultCode = faultCode;
}