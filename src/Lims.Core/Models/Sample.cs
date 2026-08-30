namespace Lims.Core.Models;

/// <summary>Sample lifecycle statuses (mirrors dbo.Samples CHECK constraint).</summary>
public static class SampleStatus
{
    public const string Registered  = "REGISTERED";
    public const string InProgress  = "IN_PROGRESS";
    public const string Completed   = "COMPLETED";
    public const string Validated   = "VALIDATED";
    public const string Rejected    = "REJECTED";
    public const string Cancelled   = "CANCELLED";

    /// <summary>Allowed manual transitions (lab workflow rules).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> AllowedTransitions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [Registered] = new[] { InProgress, Rejected, Cancelled },
            [InProgress] = new[] { Completed, Rejected, Cancelled },
            [Completed]  = new[] { Validated, Rejected },
            [Validated]  = Array.Empty<string>(),
            [Rejected]   = Array.Empty<string>(),
            [Cancelled]  = Array.Empty<string>()
        };

    public static bool CanTransition(string from, string to) =>
        AllowedTransitions.TryGetValue(from ?? string.Empty, out var targets) &&
        targets.Contains(to, StringComparer.OrdinalIgnoreCase);
}

/// <summary>A physical sample registered in the laboratory.</summary>
public class Sample
{
    public int SampleId { get; set; }
    public string SampleCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Matrix { get; set; }
    public string Status { get; set; } = SampleStatus.Registered;
    public byte Priority { get; set; } = 2;
    public string ClientCode { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public DateTime CollectedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    // ── Aggregates populated by usp_SearchSamples (via vw_SampleOverview). ──
    // These are NOT populated by usp_GetSampleByCode (which returns the full
    // Tests collection instead). Values default to 0 when not in scope.
    public int TotalTests { get; set; }
    public int CompletedTests { get; set; }
    public int PendingTests { get; set; }
    public int FailedResults { get; set; }
    public int ProgressPercent { get; set; }

    public List<SampleTest> Tests { get; set; } = new();
}

/// <summary>An analysis requested on a sample, with its result when completed.</summary>
public class SampleTest
{
    public int SampleTestId { get; set; }
    public string TestCode { get; set; } = string.Empty;
    public string TestName { get; set; } = string.Empty;
    public string? Method { get; set; }
    public string? Unit { get; set; }
    public string TestStatus { get; set; } = "PENDING";
    public string? InstrumentCode { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public decimal? ResultValue { get; set; }
    public bool? Passed { get; set; }
    public DateTime? MeasuredAt { get; set; }
    public string? Comment { get; set; }
}

/// <summary>A lab instrument (analyser) that can run tests.</summary>
public class Instrument
{
    public int InstrumentId { get; set; }
    public string InstrumentCode { get; set; } = string.Empty;
    public string InstrumentName { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? Location { get; set; }
    public DateTime? LastCalibrationAt { get; set; }
    public int CalibrationPeriodDays { get; set; }
    public DateTime? NextCalibrationDue { get; set; }
    public bool IsOverdue { get; set; }
}