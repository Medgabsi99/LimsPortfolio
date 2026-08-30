using System.Collections.Concurrent;
using Lims.Core.Interfaces;

namespace Lims.Infrastructure;

/// <summary>
/// In-memory implementation of JWT revocation (logout). Suitable for
/// single-instance deployments; a shared cache (Redis/SQL) would be the
/// multi-instance upgrade path. Expired entries are pruned lazily.
/// </summary>
public sealed class InMemoryTokenRevocationStore : ITokenRevocationStore
{
    private readonly ConcurrentDictionary<string, DateTime> _revoked = new();

    public void Revoke(string jti, DateTime expiresAtUtc)
    {
        PruneExpired();
        _revoked[jti] = expiresAtUtc;
    }

    public bool IsRevoked(string jti) =>
        _revoked.TryGetValue(jti, out var expires) && expires > DateTime.UtcNow;

    private void PruneExpired()
    {
        foreach (var (jti, expires) in _revoked)
            if (expires <= DateTime.UtcNow)
                _revoked.TryRemove(jti, out _);
    }
}