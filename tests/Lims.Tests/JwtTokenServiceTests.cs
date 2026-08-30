using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Lims.Core.Models;
using Lims.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Lims.Tests;

/// <summary>Unit tests of the JWT token issuance (authentication layer).</summary>
public class JwtTokenServiceTests
{
    private const string SigningKey =
        "unit-test-signing-key-0123456789abcdef0123456789abcdef0123456789abcdef";

    private static JwtTokenService CreateService(int expiryMinutes = 60)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "Lims.RestApi",
                ["Jwt:Audience"] = "Lims.Clients",
                ["Jwt:SigningKey"] = SigningKey,
                ["Jwt:ExpiryMinutes"] = expiryMinutes.ToString()
            })
            .Build();
        return new JwtTokenService(configuration.GetSection("Jwt"));
    }

    private static UserAccount ManagerAccount() => new()
    {
        UserId = 7,
        Username = "qual.manager",
        DisplayName = "Quality Manager",
        Role = UserRoles.Manager,
        PasswordHash = "AA",
        PasswordSalt = "BB",
        IsActive = true
    };

    [Fact]
    public void CreateToken_CarriesIdentityAndRoleClaims()
    {
        var result = CreateService().CreateToken(ManagerAccount());

        var principal = Validate(result.Token);

        Assert.Equal("qual.manager", principal.Identity!.Name);
        Assert.Equal(UserRoles.Manager, principal.FindFirst(ClaimTypes.Role)!.Value);

        // 'sub' may be mapped to ClaimTypes.NameIdentifier by the inbound claim map
        var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        Assert.Equal("7", subject);
    }

    [Fact]
    public void CreateToken_ReturnsUserMetadata()
    {
        var result = CreateService().CreateToken(ManagerAccount());

        Assert.Equal("qual.manager", result.Username);
        Assert.Equal("Quality Manager", result.DisplayName);
        Assert.Equal(UserRoles.Manager, result.Role);
        Assert.True(result.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public void CreateToken_HonoursConfiguredExpiry()
    {
        var result = CreateService(expiryMinutes: 30).CreateToken(ManagerAccount());

        var remaining = result.ExpiresAtUtc - DateTime.UtcNow;
        Assert.InRange(remaining.TotalMinutes, 28, 31);
    }

    [Fact]
    public void CreateToken_MissingSigningKey_Throws()
    {
        var service = new JwtTokenService(
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Jwt:Issuer"] = "Lims.RestApi" }).Build().GetSection("Jwt"));

        Assert.Throws<InvalidOperationException>(() => service.CreateToken(ManagerAccount()));
    }

    /// <summary>Validates a token with the same parameters as the REST API.</summary>
    private static ClaimsPrincipal Validate(string token)
    {
        return new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "Lims.RestApi",
            ValidateAudience = true,
            ValidAudience = "Lims.Clients",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            ValidateLifetime = false,
            ClockSkew = TimeSpan.Zero
        }, out _);
    }
}