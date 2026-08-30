using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Lims.Core.Interfaces;
using Lims.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Lims.Infrastructure;

/// <summary>
/// Issues signed JWT bearer tokens for validated lab accounts.
/// The Jwt configuration section is read lazily so that the SOAP service and
/// the Windows Service can reuse the infrastructure layer without a Jwt config.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private readonly IConfigurationSection _options;

    public JwtTokenService(IConfigurationSection options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public AuthTokenResult CreateToken(UserAccount user)
    {
        var signingKey = _options["SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
            throw new InvalidOperationException(
                "Jwt:SigningKey is missing or too short (at least 32 characters required).");

        var issuer = _options["Issuer"] ?? "Lims.RestApi";
        var audience = _options["Audience"] ?? "Lims.Clients";
        var expiryMinutes = _options["ExpiryMinutes"] is { } raw && int.TryParse(raw, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 480;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString(CultureInfo.InvariantCulture)),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            // Security stamp: rejected at validation time when the account's
            // TokenVersion has moved (password change/reset/deactivation).
            new Claim("ver", user.TokenVersion.ToString(CultureInfo.InvariantCulture)),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        // notBefore slightly in the past : absorbs small clock differences
        // between the API host and the client validating the token.
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(expiryMinutes);
        var token = new JwtSecurityToken(issuer, audience, claims,
            notBefore: now.AddSeconds(-5), expires: expires, signingCredentials: credentials);

        return new AuthTokenResult
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            Username = user.Username,
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
            Role = user.Role,
            ExpiresAtUtc = expires
        };
    }
}