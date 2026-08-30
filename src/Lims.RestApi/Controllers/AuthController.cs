using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Lims.Core.Interfaces;
using Lims.Core.Models;
using Lims.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lims.RestApi.Controllers;

/// <summary>
/// Authentication endpoint for the lab front-end.
/// Issues short-lived JWT bearer tokens used by every other API endpoint.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly IAuditRepository _audit;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IUserRepository users, IAuditRepository audit,
        IPasswordHasher hasher, ITokenService tokens, ILogger<AuthController> logger)
    {
        _users = users;
        _audit = audit;
        _hasher = hasher;
        _tokens = tokens;
        _logger = logger;
    }

    /// <summary>Authenticate a lab user and return a JWT bearer token.</summary>
    /// <remarks>
    /// Roles: **Analyst** can register samples and submit results,
    /// **Manager** can additionally validate or reject samples.
    /// Failed attempts are written to the audit trail (regulatory-friendly).
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthTokenResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Username and password are required." });

        var account = await _users.GetByUsernameAsync(request.Username.Trim());
        if (account is null || !account.IsActive ||
            !_hasher.Verify(request.Password, account.PasswordSalt, account.PasswordHash))
        {
            _logger.LogWarning("Failed sign-in for {Username}", request.Username);
            await _audit.LogAsync("REST_API", "LOGIN", request.Username, false, "Invalid credentials");
            return Unauthorized(new { error = "Invalid username or password." });
        }

        var token = _tokens.CreateToken(account);
        await _audit.LogAsync("REST_API", "LOGIN", account.Username, true, $"Role={account.Role}");
        _logger.LogInformation("User {Username} ({Role}) signed in", account.Username, account.Role);
        return Ok(token);
    }

    /// <summary>Profile of the authenticated user, read from the JWT claims.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me() => Ok(new
    {
        username = User.Identity?.Name,
        role = User.FindFirstValue(ClaimTypes.Role)
    });

    /// <summary>
    /// Self-service password change (current password required). Bumps the
    /// account's TokenVersion: every other token of this user is revoked.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var errors = DomainValidators.ValidatePassword(request.NewPassword);
        if (errors.Count > 0)
            return BadRequest(new { errors });

        var username = User.Identity?.Name;
        var account = username is null ? null : await _users.GetByUsernameAsync(username);
        if (account is null || !account.IsActive ||
            !_hasher.Verify(request.CurrentPassword, account.PasswordSalt, account.PasswordHash))
        {
            await _audit.LogAsync("REST_API", "PASSWORD_CHANGE", username, false, "Current password invalid");
            return Unauthorized(new { error = "Current password is incorrect." });
        }

        var salt = _hasher.NewSalt();
        await _users.ChangePasswordAsync(account.UserId, _hasher.Hash(request.NewPassword, salt), salt,
            account.Username);

        _logger.LogInformation("User {Username} changed their password", account.Username);
        return NoContent();
    }

    /// <summary>
    /// Server-side logout: revokes the presented token's jti until its natural
    /// expiry. The client also discards the token. Password changes and
    /// deactivations revoke all of a user's tokens via the version stamp.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        [FromServices] ITokenRevocationStore revocation)
    {
        var jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        var exp = User.FindFirstValue("exp");
        if (jti is not null)
        {
            var expiresAt = long.TryParse(exp, out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
                : DateTime.UtcNow.AddMinutes(10);
            revocation.Revoke(jti, expiresAt);
        }

        await _audit.LogAsync("REST_API", "LOGOUT", User.Identity?.Name, true, null);
        return NoContent();
    }

    /// <summary>
    /// Anonymous "request access" form: logs a pending-access request to the
    /// audit trail so a Manager can action it via the Users admin panel.
    /// </summary>
    [HttpPost("request-access")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestAccess([FromBody] AccessRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { error = "Full name and email are required." });

        var detail = $"Name={request.FullName.Trim()} Email={request.Email.Trim()} Reason={request.Reason?.Trim()}";
        await _audit.LogAsync("REST_API", "REQUEST_ACCESS", request.Email.Trim(), true, detail);
        _logger.LogInformation("Access requested by {Email}", request.Email);
        return Accepted(new { message = "Your request has been received. A manager will review it shortly." });
    }

    public record LoginRequest(string Username, string Password);
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public record AccessRequest(string FullName, string Email, string? Reason);
}