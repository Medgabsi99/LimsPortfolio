namespace Lims.Core.Models;

/// <summary>
/// Role names carried in the JWT claims and used by the [Authorize] attributes.
/// Analysts register samples and submit results; Managers validate or reject them.
/// </summary>
public static class UserRoles
{
    public const string Analyst = "Analyst";
    public const string Manager = "Manager";

    /// <summary>Value for [Authorize(Roles = ...)] : any authenticated lab user.</summary>
    public const string AnalystOrManager = Analyst + "," + Manager;
}

/// <summary>A lab user account (dbo.Users). Password material is PBKDF2, never clear text.</summary>
public class UserAccount
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Role { get; set; } = UserRoles.Analyst;

    /// <summary>PBKDF2-SHA256 hash, hex-encoded (64 chars). Not loaded by the admin list query.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Per-user random salt, hex-encoded (32 chars). Not loaded by the admin list query.</summary>
    public string PasswordSalt { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Security stamp carried as a JWT claim. Incremented on password change /
    /// reset / deactivation; tokens stamped with an older version are rejected.
    /// </summary>
    public int TokenVersion { get; set; } = 1;

    /// <summary>UTC creation timestamp (loaded by the admin queries).</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>JWT issued by POST /api/auth/login.</summary>
public class AuthTokenResult
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}