using Lims.Core.Models;

namespace Lims.Core.Interfaces;

/// <summary>Issues authentication tokens for validated lab accounts.</summary>
public interface ITokenService
{
    /// <summary>Creates a signed JWT carrying the user identity and role.</summary>
    AuthTokenResult CreateToken(UserAccount user);
}

/// <summary>
/// Server-side revocation of individual JWTs (logout). Stores the token's jti
/// until its natural expiry. Complements the TokenVersion security stamp, which
/// revokes ALL tokens of a user at once.
/// </summary>
public interface ITokenRevocationStore
{
    /// <summary>Revoke a token id until the given expiry (usually the token's exp).</summary>
    void Revoke(string jti, DateTime expiresAtUtc);

    /// <summary>True if the token id has been revoked (and not yet expired).</summary>
    bool IsRevoked(string jti);
}