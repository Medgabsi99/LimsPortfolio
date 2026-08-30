using System.Security.Cryptography;
using Lims.Core.Interfaces;

namespace Lims.Core.Services;

/// <summary>
/// PBKDF2-SHA256 password hashing (100,000 iterations, per-user salt).
/// Pure .NET cryptography - no external dependency, unit-testable, and the
/// exact algorithm used by the SQL seed data (see database/04_seed_data.sql).
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    public const int Iterations = 100_000;
    private const int SaltSizeBytes = 16;
    private const int KeySizeBytes = 32;

    public string NewSalt() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(SaltSizeBytes));

    public string Hash(string password, string saltHex) =>
        Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(
            password, Convert.FromHexString(saltHex), Iterations, HashAlgorithmName.SHA256, KeySizeBytes));

    public bool Verify(string password, string saltHex, string expectedHashHex)
    {
        var actual = Convert.FromHexString(Hash(password, saltHex));
        var expected = Convert.FromHexString(expectedHashHex);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}