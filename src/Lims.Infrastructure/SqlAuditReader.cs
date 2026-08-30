using Dapper;
using Lims.Core.Interfaces;

namespace Lims.Infrastructure;

/// <summary>
/// SQL Server implementation of ISqlAuditReader.
/// Provides paged read access to dbo.AuditLog (usp_GetAuditLog)
/// and active clients list (usp_GetClients) for dropdown population.
/// </summary>
public sealed class SqlAuditReader : ISqlAuditReader
{
    private readonly ISqlConnectionFactory _factory;

    public SqlAuditReader(ISqlConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public async Task<AuditPagedResult> SearchAuditLogAsync(
        string? searchText, bool? isSuccess,
        DateTime? fromDate, DateTime? toDate,
        int page, int pageSize,
        CancellationToken ct = default)
    {
        await using var conn = _factory.Create();

        using var multi = await conn.QueryMultipleAsync(
            new CommandDefinition("dbo.usp_GetAuditLog",
                new { SearchText = searchText, IsSuccess = isSuccess,
                      FromDate = fromDate, ToDate = toDate,
                      PageNumber = page, PageSize = pageSize },
                commandType: System.Data.CommandType.StoredProcedure,
                cancellationToken: ct));

        var items = (await multi.ReadAsync<AuditEntry>()).ToList();
        var total = await multi.ReadFirstAsync<int>();

        return new AuditPagedResult
        {
            Items      = items,
            TotalCount = total,
            PageNumber = page,
            PageSize   = pageSize
        };
    }

    public async Task<IReadOnlyList<ClientDto>> GetClientsAsync(CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        var rows = await conn.QueryAsync<ClientDto>(
            new CommandDefinition("dbo.usp_GetClients",
                commandType: System.Data.CommandType.StoredProcedure,
                cancellationToken: ct));
        return rows.ToList();
    }
}
