using Lims.Core.Models;

namespace Lims.Core.Interfaces;

/// <summary>
/// Persistence contract for the LIMS sample domain.
/// Implementations call the SQL Server stored procedures (see database/03_stored_procedures.sql).
/// </summary>
public interface ISampleRepository
{
    /// <summary>Registers a sample and returns its generated code (usp_CreateSample).</summary>
    Task<(string SampleCode, int SampleId)> CreateSampleAsync(CreateSampleRequest request, string createdBy, CancellationToken ct = default);

    /// <summary>Loads a sample with all its tests and results (usp_GetSampleByCode).</summary>
    Task<Sample?> GetSampleByCodeAsync(string sampleCode, CancellationToken ct = default);

    /// <summary>Paged sample search (usp_SearchSamples).</summary>
    Task<PagedResult<Sample>> SearchSamplesAsync(SampleSearchFilter filter, CancellationToken ct = default);

    /// <summary>Records a result and rolls the workflow forward (usp_SubmitResult).</summary>
    Task<ResultSubmissionResult> SubmitResultAsync(ResultSubmission submission, CancellationToken ct = default);

    /// <summary>Manual status transition with audit trail (usp_ChangeSampleStatus).</summary>
    Task ChangeSampleStatusAsync(string sampleCode, string newStatus, string? comment, string changedBy, CancellationToken ct = default);

    /// <summary>Dashboard aggregates (usp_GetDashboardStats).</summary>
    Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct = default);

    /// <summary>Instruments with calibration status (vw_InstrumentCalibration).</summary>
    Task<IReadOnlyList<Instrument>> GetInstrumentsAsync(CancellationToken ct = default);

    /// <summary>Writes a technical audit entry (usp_LogAudit).</summary>
    Task LogAuditAsync(string source, string action, string? entityRef, bool isSuccess, string? message, CancellationToken ct = default);
}