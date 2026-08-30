namespace Lims.Core.Interfaces;

/// <summary>
/// Writes regulatory audit trail entries.
/// Separated from <see cref=""ISampleRepository""/> so that authentication
/// and user-management code does not carry a dependency on the sample domain.
/// </summary>
public interface IAuditRepository
{
    /// <summary>
    /// Appends a row to <c>dbo.AuditLog</c> via <c>usp_LogAudit</c>.
    /// </summary>
    /// <param name=""source"">Originating subsystem: REST_API, SOAP, WINDOWS_SVC…</param>
    /// <param name=""action"">Verb: LOGIN, LOGOUT, PASSWORD_CHANGE, CREATE_SAMPLE…</param>
    /// <param name=""entityRef"">Username, SampleCode, or other primary reference.</param>
    /// <param name=""isSuccess"">Whether the operation succeeded.</param>
    /// <param name=""message"">Optional free-text detail (reason for failure, role, etc.).</param>
    Task LogAsync(string source, string action, string? entityRef,
                  bool isSuccess, string? message,
                  CancellationToken ct = default);
}
