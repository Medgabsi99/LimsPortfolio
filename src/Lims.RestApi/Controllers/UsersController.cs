using Lims.Core.Interfaces;
using Lims.Core.Models;
using Lims.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lims.RestApi.Controllers;

/// <summary>
/// User account administration - Manager role only.
/// Account creation, activation/deactivation and password resets; every write
/// is audited and bumps the user's TokenVersion so issued tokens are revoked.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = UserRoles.Manager)]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserRepository users, IPasswordHasher hasher, ILogger<UsersController> logger)
    {
        _users = users;
        _hasher = hasher;
        _logger = logger;
    }

    /// <summary>List all lab accounts (no password material).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserAccount>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserAccount>>> List()
    {
        var users = await _users.ListAsync();
        return Ok(users.Select(u => new
        {
            u.UserId,
            u.Username,
            u.DisplayName,
            u.Role,
            u.IsActive,
            u.CreatedAt
        }));
    }

    /// <summary>Create a new lab account (Analyst or Manager).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var errors = DomainValidators.ValidateNewUser(request.Username, request.DisplayName, request.Role)
            .Concat(DomainValidators.ValidatePassword(request.Password))
            .ToList();
        if (errors.Count > 0)
            return BadRequest(new { errors });

        var account = new UserAccount
        {
            Username = request.Username.Trim(),
            DisplayName = request.DisplayName.Trim(),
            Role = request.Role,
            IsActive = true
        };
        var salt = _hasher.NewSalt();
        account.PasswordSalt = salt;
        account.PasswordHash = _hasher.Hash(request.Password, salt);

        int userId;
        try
        {
            userId = await _users.CreateAsync(account, User.Identity?.Name ?? "api");
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
        {
            return Conflict(new { message = $"Username '{account.Username}' already exists." });
        }

        _logger.LogInformation("Account {Username} ({Role}) created by {By}", account.Username, account.Role, User.Identity?.Name);
        return CreatedAtAction(nameof(List), new { userId }, new { userId, account.Username, account.Role });
    }

    /// <summary>Activate or deactivate an account. Deactivation revokes its tokens immediately.</summary>
    [HttpPut("{userId:int}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActive(int userId, [FromBody] SetActiveRequest body)
    {
        var target = await _users.GetByIdAsync(userId);
        if (target is null)
            return NotFound(new { message = $"User {userId} not found." });

        // Guard: the last active Manager cannot lock the whole lab out.
        if (target.Role == UserRoles.Manager && target.IsActive && !body.IsActive)
        {
            var users = await _users.ListAsync();
            if (users.Count(u => u.Role == UserRoles.Manager && u.IsActive) <= 1)
                return BadRequest(new { message = "Cannot deactivate the last active Manager account." });
        }

        // Guard: a manager cannot deactivate their own account.
        if (target.Username == User.Identity?.Name && !body.IsActive)
            return BadRequest(new { message = "You cannot deactivate your own account." });

        await _users.SetActiveAsync(userId, body.IsActive, User.Identity?.Name ?? "api");
        _logger.LogInformation("User {Username} IsActive={IsActive} set by {By}", target.Username, body.IsActive, User.Identity?.Name);
        return NoContent();
    }

    /// <summary>Reset a user's password (no old password required). Revokes the user's tokens.</summary>
    [HttpPut("{userId:int}/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(int userId, [FromBody] ResetPasswordRequest body)
    {
        var errors = DomainValidators.ValidatePassword(body.NewPassword);
        if (errors.Count > 0)
            return BadRequest(new { errors });

        var salt = _hasher.NewSalt();
        var hash = _hasher.Hash(body.NewPassword, salt);
        if (!await _users.ResetPasswordAsync(userId, hash, salt, User.Identity?.Name ?? "api"))
            return NotFound(new { message = $"User {userId} not found." });

        _logger.LogInformation("Password of user {UserId} reset by {By}", userId, User.Identity?.Name);
        return NoContent();
    }

    public record CreateUserRequest(string Username, string DisplayName, string Role, string Password);
    public record SetActiveRequest(bool IsActive);
    public record ResetPasswordRequest(string NewPassword);
}