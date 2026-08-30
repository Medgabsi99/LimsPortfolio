using Dapper;
using Lims.Core.Interfaces;

namespace Lims.Infrastructure;

/// <summary>
/// SQL Server implementation of <see cref="IAuditRepository"/>.
/// Delegates to <c>dbo.usp_LogAudit</c> — the same SP used by the
/// sample repository for sample-lifecycle events.
/// </summary>
public sealed class AuditRepository : IAuditRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public AuditRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task LogAsync(string source, string action, string? entityRef,
        bool isSuccess, string? message, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.ExecuteAsync(
            new CommandDefinition("dbo.usp_LogAudit",
                new { Source = source, Action = action, EntityRef = entityRef, IsSuccess = isSuccess, Message = message },
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));
    }
}
