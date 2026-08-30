using Lims.Core.Models;

namespace Lims.Core.Interfaces;

/// <summary>Persistence contract for lab user accounts (dbo.Users).</summary>
public interface IUserRepository
{
    /// <summary>Loads an account by username, hash material included (usp_GetUserByUsername).</summary>
    Task<UserAccount?> GetByUsernameAsync(string username, CancellationToken ct = default);

    /// <summary>Loads an account by primary key, without hash material (usp_GetUserById).</summary>
    Task<UserAccount?> GetByIdAsync(int userId, CancellationToken ct = default);

    /// <summary>Lists all accounts WITHOUT hash material (usp_ListUsers).</summary>
    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken ct = default);

    /// <summary>Creates an account (hash + salt already computed) and returns the new UserId (usp_CreateUser).</summary>
    Task<int> CreateAsync(UserAccount account, string createdBy, CancellationToken ct = default);

    /// <summary>Activates / deactivates an account and revokes its tokens (usp_SetUserActive). False if not found.</summary>
    Task<bool> SetActiveAsync(int userId, bool isActive, string changedBy, CancellationToken ct = default);

    /// <summary>Self-service password change; revokes existing tokens (usp_ChangePassword). False if not found.</summary>
    Task<bool> ChangePasswordAsync(int userId, string newHash, string newSalt, string changedBy, CancellationToken ct = default);

    /// <summary>Manager-driven password reset; revokes existing tokens (usp_ResetPassword). False if not found.</summary>
    Task<bool> ResetPasswordAsync(int userId, string newHash, string newSalt, string changedBy, CancellationToken ct = default);
}