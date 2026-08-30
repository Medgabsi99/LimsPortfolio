namespace Lims.Core.Interfaces;

/// <summary>Password hashing contract (PBKDF2-SHA256, constant-time verification).</summary>
public interface IPasswordHasher
{
    /// <summary>Generates a new random per-user salt (hex).</summary>
    string NewSalt();

    /// <summary>Computes the PBKDF2-SHA256 hash (hex) of a password for the given salt.</summary>
    string Hash(string password, string saltHex);

    /// <summary>Verifies a password against its stored hash (timing-safe comparison).</summary>
    bool Verify(string password, string saltHex, string expectedHashHex);
}