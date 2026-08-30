using Dapper;
using Lims.Core.Interfaces;
using Lims.Core.Models;

namespace Lims.Infrastructure;

/// <summary>
/// SQL Server implementation of the user account persistence (dbo.Users).
/// Same convention as SampleRepository: stored procedure access only.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public UserRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<UserAccount?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();

        return await conn.QueryFirstOrDefaultAsync<UserAccount>(
            new CommandDefinition("dbo.usp_GetUserByUsername",
                new { Username = username },
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));
    }

    public async Task<UserAccount?> GetByIdAsync(int userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();

        return await conn.QueryFirstOrDefaultAsync<UserAccount>(
            new CommandDefinition("dbo.usp_GetUserById",
                new { UserId = userId },
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();

        var rows = await conn.QueryAsync<UserAccount>(
            new CommandDefinition("dbo.usp_ListUsers",
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<int> CreateAsync(UserAccount account, string createdBy, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();

        var userId = await conn.QuerySingleAsync<int>(
            new CommandDefinition("dbo.usp_CreateUser",
                new
                {
                    account.Username,
                    DisplayName = account.DisplayName ?? account.Username,
                    account.Role,
                    account.PasswordHash,
                    account.PasswordSalt,
                    CreatedBy = createdBy
                },
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));
        return userId;
    }

    public async Task<bool> SetActiveAsync(int userId, bool isActive, string changedBy, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();

        // The procedure returns its row count explicitly: SET NOCOUNT ON makes
        // ExecuteNonQuery's rows-affected value unreliable (-1).
        var rows = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition("dbo.usp_SetUserActive",
                new { UserId = userId, IsActive = isActive, ChangedBy = changedBy },
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> ChangePasswordAsync(int userId, string newHash, string newSalt, string changedBy, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();

        var rows = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition("dbo.usp_ChangePassword",
                new { UserId = userId, NewHash = newHash, NewSalt = newSalt, ChangedBy = changedBy },
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> ResetPasswordAsync(int userId, string newHash, string newSalt, string changedBy, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();

        var rows = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition("dbo.usp_ResetPassword",
                new { UserId = userId, NewHash = newHash, NewSalt = newSalt, ChangedBy = changedBy },
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));
        return rows > 0;
    }
}