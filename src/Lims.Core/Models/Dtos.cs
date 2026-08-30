using System.ComponentModel.DataAnnotations;

namespace Lims.Core.Models;

/// <summary>Request payload for registering a new sample (REST POST / SOAP).</summary>
public class CreateSampleRequest
{
    [Required(ErrorMessage = "ClientCode is required.")]
    [StringLength(20, ErrorMessage = "ClientCode cannot exceed 20 characters.")]
    public string ClientCode { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }

    [StringLength(100, ErrorMessage = "Matrix cannot exceed 100 characters.")]
    public string? Matrix { get; set; }

    [Range(1, 3, ErrorMessage = "Priority must be 1 (High), 2 (Normal) or 3 (Low).")]
    public byte Priority { get; set; } = 2;

    /// <summary>Requested test codes, e.g. ["PH", "ASSAY"].</summary>
    [Required(ErrorMessage = "At least one TestCode is required.")]
    [MinLength(1, ErrorMessage = "At least one TestCode is required.")]
    public List<string> TestCodes { get; set; } = new();
}

/// <summary>An analytical result submitted by an analyst or an instrument.</summary>
public class ResultSubmission
{
    public string SampleCode { get; set; } = string.Empty;   // set from route, not body

    [Required(ErrorMessage = "TestCode is required.")]
    [StringLength(20, ErrorMessage = "TestCode cannot exceed 20 characters.")]
    public string TestCode { get; set; } = string.Empty;

    public decimal ResultValue { get; set; }

    [StringLength(20, ErrorMessage = "InstrumentCode cannot exceed 20 characters.")]
    public string? InstrumentCode { get; set; }

    [StringLength(500, ErrorMessage = "Comment cannot exceed 500 characters.")]
    public string? Comment { get; set; }

    public string Source { get; set; } = "REST_API";
    public DateTime? MeasuredAt { get; set; }
}


/// <summary>Outcome of a result submission.</summary>
public class ResultSubmissionResult
{
    public bool Passed { get; set; }
    public string SampleStatus { get; set; } = string.Empty;
}

/// <summary>Paged search result.</summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Search filters for the sample register.</summary>
public class SampleSearchFilter
{
    public string? SearchText { get; set; }
    public string? Status { get; set; }
    public string? ClientCode { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>Dashboard aggregates (single stored-procedure round trip).</summary>
public class DashboardStats
{
    public List<StatusCount> SamplesByStatus { get; set; } = new();
    public List<Instrument> OverdueCalibrations { get; set; } = new();
    public List<OutOfSpecStat> OutOfSpecTests { get; set; } = new();

    public class StatusCount
    {
        public string Status { get; set; } = string.Empty;
        public int SampleCount { get; set; }
    }

    public class OutOfSpecStat
    {
        public string TestCode { get; set; } = string.Empty;
        public string TestName { get; set; } = string.Empty;
        public int OutOfSpecCount { get; set; }
    }
}