namespace Lims.Core.Interfaces;

/// <summary>Read-only access to dbo.AuditLog and dbo.Clients for reporting panels.</summary>
public interface ISqlAuditReader
{
    Task<AuditPagedResult> SearchAuditLogAsync(
        string? searchText, bool? isSuccess,
        DateTime? fromDate, DateTime? toDate,
        int page, int pageSize,
        CancellationToken ct = default);

    Task<IReadOnlyList<ClientDto>> GetClientsAsync(CancellationToken ct = default);
}

/// <summary>One row from dbo.AuditLog.</summary>
public class AuditEntry
{
    public long    AuditId   { get; set; }
    public string  Source    { get; set; } = string.Empty;
    public string  Action    { get; set; } = string.Empty;
    public string? EntityRef { get; set; }
    public bool    IsSuccess { get; set; }
    public string? Message   { get; set; }
    public DateTime LoggedAt { get; set; }
}

/// <summary>Paged audit log result.</summary>
public class AuditPagedResult
{
    public IReadOnlyList<AuditEntry> Items { get; set; } = Array.Empty<AuditEntry>();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize   { get; set; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Client summary for dropdown population.</summary>
public class ClientDto
{
    public int    ClientId     { get; set; }
    public string ClientCode   { get; set; } = string.Empty;
    public string CompanyName  { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
}
