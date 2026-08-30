using Lims.Core.Interfaces;
using Lims.Core.Models;
using Lims.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lims.RestApi.Controllers;

/// <summary>
/// REST endpoint for the sample lifecycle.
/// Base route: /api/samples
///
/// Role-based authorization (JWT):
///   - Analyst or Manager : register samples, submit results
///   - Manager only       : manual status transitions (validation / rejection)
/// </summary>
[ApiController]
[Route("api/samples")]
[Authorize]
[Produces("application/json")]
public class SamplesController : ControllerBase
{
    private readonly ISampleRepository _repository;
    private readonly ILogger<SamplesController> _logger;

    public SamplesController(ISampleRepository repository, ILogger<SamplesController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>Search the sample register (paged).</summary>
    /// <param name="searchText">Free text on sample code / description / client.</param>
    /// <param name="status">Optional status filter (REGISTERED, IN_PROGRESS...).</param>
    /// <param name="clientCode">Optional client filter.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Page size (max 200).</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<Sample>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<Sample>>> Search(
        [FromQuery] string? searchText,
        [FromQuery] string? status,
        [FromQuery] string? clientCode,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _repository.SearchSamplesAsync(new SampleSearchFilter
        {
            SearchText = searchText,
            Status = status,
            ClientCode = clientCode,
            PageNumber = page,
            PageSize = pageSize
        });
        return Ok(result);
    }

    /// <summary>Get one sample with all its tests and results.</summary>
    [HttpGet("{sampleCode}", Name = nameof(GetByCode))]
    [ProducesResponseType(typeof(Sample), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Sample>> GetByCode(string sampleCode)
    {
        var sample = await _repository.GetSampleByCodeAsync(sampleCode);
        return sample is null ? NotFound(new { message = $"Sample '{sampleCode}' not found." }) : Ok(sample);
    }

    /// <summary>Register a new sample (generates the sample code).</summary>
    [HttpPost]
    [Authorize(Roles = UserRoles.AnalystOrManager)]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create([FromBody] CreateSampleRequest request)
    {
        var errors = DomainValidators.Validate(request);
        if (errors.Count > 0)
            return BadRequest(new { errors });

        var createdBy = User.Identity?.Name ?? "api";
        var (sampleCode, sampleId) = await _repository.CreateSampleAsync(request, createdBy);

        _logger.LogInformation("Sample {SampleCode} created for client {ClientCode}", sampleCode, request.ClientCode);
        return CreatedAtRoute(nameof(GetByCode), new { sampleCode }, new { sampleCode, sampleId });
    }

    /// <summary>Submit an analytical result for a sample test.</summary>
    [HttpPost("{sampleCode}/results")]
    [Authorize(Roles = UserRoles.AnalystOrManager)]
    [ProducesResponseType(typeof(ResultSubmissionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SubmitResult(string sampleCode, [FromBody] ResultSubmission submission)
    {
        submission.SampleCode = sampleCode;   // route wins over body
        submission.Source = "REST_API";

        var errors = DomainValidators.Validate(submission);
        if (errors.Count > 0)
            return BadRequest(new { errors });

        var result = await _repository.SubmitResultAsync(submission);
        return Ok(result);
    }

    /// <summary>Manual status transition (validation, rejection...) - Manager role only.</summary>
    [HttpPut("{sampleCode}/status")]
    [Authorize(Roles = UserRoles.Manager)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ChangeStatus(string sampleCode,
        [FromBody] ChangeStatusRequest body)
    {
        var sample = await _repository.GetSampleByCodeAsync(sampleCode);
        if (sample is null)
            return NotFound(new { message = $"Sample '{sampleCode}' not found." });

        if (!DomainValidators.IsTransitionAllowed(sample.Status, body.NewStatus))
            return BadRequest(new
            {
                message = $"Transition {sample.Status} -> {body.NewStatus} is not allowed.",
                allowed = SampleStatus.AllowedTransitions.GetValueOrDefault(sample.Status, Array.Empty<string>())
            });

        await _repository.ChangeSampleStatusAsync(sampleCode, body.NewStatus, body.Comment,
            User.Identity?.Name ?? "api");
        return NoContent();
    }

    /// <summary>Dashboard aggregates (counts, overdue calibrations, out-of-spec).</summary>
    [HttpGet("~/api/dashboard")]
    [ProducesResponseType(typeof(DashboardStats), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardStats>> Dashboard()
    {
        return Ok(await _repository.GetDashboardStatsAsync());
    }

    public record ChangeStatusRequest(string NewStatus, string? Comment);
}