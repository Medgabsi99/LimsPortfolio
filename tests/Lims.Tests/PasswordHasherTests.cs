using Lims.Core.Services;
using Xunit;

namespace Lims.Tests;

/// <summary>Unit tests of the PBKDF2-SHA256 password hashing service.</summary>
public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    // Salt + hash of the seeded account analyst1 (database/04_seed_data.sql).
    private const string SeedSalt = "6F2A9D41C85B3E70A1D4F8C29B5E3716";
    private const string SeedHash = "61422209B93A100B656CBFDEBA0D28F03F59099189C5DA65A837E71AECDFD1EB";

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        Assert.True(_hasher.Verify("Analyst@2026", SeedSalt, SeedHash));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        Assert.False(_hasher.Verify("WrongPassword!", SeedSalt, SeedHash));
    }

    [Fact]
    public void Verify_WrongSalt_ReturnsFalse()
    {
        Assert.False(_hasher.Verify("Analyst@2026", "00000000000000000000000000000000", SeedHash));
    }

    [Fact]
    public void Hash_IsDeterministic_ForSameSaltAndPassword()
    {
        Assert.Equal(_hasher.Hash("some password", SeedSalt), _hasher.Hash("some password", SeedSalt));
    }

    [Fact]
    public void Hash_Produces64HexCharacters()
    {
        var hash = _hasher.Hash("Analyst@2026", SeedSalt);
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9A-F]+$", hash);
    }

    [Fact]
    public void NewSalt_GeneratesUniqueHexSalts()
    {
        var first = _hasher.NewSalt();
        var second = _hasher.NewSalt();

        Assert.Equal(32, first.Length);           // 16 bytes -> 32 hex chars
        Assert.Matches("^[0-9A-F]+$", first);
        Assert.NotEqual(first, second);
    }
}