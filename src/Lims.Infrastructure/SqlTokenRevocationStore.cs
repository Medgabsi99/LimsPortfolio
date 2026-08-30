using Dapper;
using Lims.Core.Interfaces;

namespace Lims.Infrastructure;

/// <summary>
/// SQL Server-backed implementation of JWT revocation (logout).
/// Persists revoked JTIs in <c>dbo.RevokedTokens</c> so that revocations
/// survive service restarts and work correctly in multi-instance deployments.
///
/// For single-instance deployments where restart-persistence is not required,
/// <see cref="InMemoryTokenRevocationStore"/> can be used instead and avoids
/// a DB round-trip on every authenticated request.
/// </summary>
public sealed class SqlTokenRevocationStore : ITokenRevocationStore
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public SqlTokenRevocationStore(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc/>
    public void Revoke(string jti, DateTime expiresAtUtc)
    {
        // Fire-and-forget is acceptable here: a failed revoke write is
        // preferable to blocking the HTTP response. The in-memory fallback
        // in AuthController already cleared the local session on the client.
        Task.Run(async () =>
        {
            try
            {
                await using var conn = _connectionFactory.Create();
                await conn.ExecuteAsync(
                    new CommandDefinition("dbo.usp_RevokeToken",
                        new { Jti = jti, ExpiresAtUtc = expiresAtUtc },
                        commandType: System.Data.CommandType.StoredProcedure));
            }
            catch
            {
                // Swallow: a failed revocation write should not crash the request.
            }
        });
    }

    /// <inheritdoc/>
    public bool IsRevoked(string jti)
    {
        // Synchronous wrapper — ITokenRevocationStore.IsRevoked() is called
        // inside the JWT validation pipeline which is synchronous.
        try
        {
            using var conn = _connectionFactory.Create();
            var p = new DynamicParameters();
            p.Add("@Jti", jti);
            p.Add("@IsRevoked", dbType: System.Data.DbType.Boolean,
                  direction: System.Data.ParameterDirection.Output);
            conn.Execute(
                new CommandDefinition("dbo.usp_IsTokenRevoked", p,
                    commandType: System.Data.CommandType.StoredProcedure));
            return p.Get<bool>("@IsRevoked");
        }
        catch
        {
            // On DB error, fail open — TokenVersion is the authoritative bulk revocation.
            return false;
        }
    }
}

