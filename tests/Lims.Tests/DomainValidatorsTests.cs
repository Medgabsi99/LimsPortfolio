using Lims.Core.Models;
using Lims.Core.Services;
using Xunit;

namespace Lims.Tests;

/// <summary>Unit tests for the LIMS business rules (workflow + spec evaluation).</summary>
public class DomainValidatorsTests
{
    // ---- Sample creation ---------------------------------------------------

    [Fact]
    public void ValidateCreate_WithValidRequest_ReturnsNoErrors()
    {
        var request = new CreateSampleRequest
        {
            ClientCode = "CLI-001",
            Matrix = "Water",
            Priority = 2,
            TestCodes = new List<string> { "PH", "ASSAY" }
        };

        Assert.Empty(DomainValidators.Validate(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateCreate_WithoutClient_ReturnsError(string? clientCode)
    {
        var request = new CreateSampleRequest
        {
            ClientCode = clientCode!,
            TestCodes = new List<string> { "PH" }
        };

        Assert.Contains(DomainValidators.Validate(request), e => e.Contains("Client code"));
    }

    [Fact]
    public void ValidateCreate_WithoutTests_ReturnsError()
    {
        var request = new CreateSampleRequest { ClientCode = "CLI-001", TestCodes = new List<string>() };

        Assert.Contains(DomainValidators.Validate(request), e => e.Contains("At least one test"));
    }

    [Fact]
    public void ValidateCreate_WithDuplicateTests_ReturnsError()
    {
        var request = new CreateSampleRequest
        {
            ClientCode = "CLI-001",
            TestCodes = new List<string> { "PH", "ph " }
        };

        Assert.Contains(DomainValidators.Validate(request), e => e.Contains("Duplicate"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void ValidateCreate_WithInvalidPriority_ReturnsError(byte priority)
    {
        var request = new CreateSampleRequest
        {
            ClientCode = "CLI-001",
            Priority = priority,
            TestCodes = new List<string> { "PH" }
        };

        Assert.Contains(DomainValidators.Validate(request), e => e.Contains("Priority"));
    }

    // ---- Result submission ---------------------------------------------------

    [Fact]
    public void ValidateResult_WithNegativeValue_ReturnsError()
    {
        var submission = new ResultSubmission
        {
            SampleCode = "SMP-2026-00001",
            TestCode = "PH",
            ResultValue = -1m
        };

        Assert.Contains(DomainValidators.Validate(submission), e => e.Contains("negative"));
    }

    // ---- Workflow transitions ------------------------------------------------

    [Theory]
    [InlineData(SampleStatus.Registered, SampleStatus.InProgress, true)]
    [InlineData(SampleStatus.InProgress, SampleStatus.Completed, true)]
    [InlineData(SampleStatus.Completed, SampleStatus.Validated, true)]
    [InlineData(SampleStatus.Completed, SampleStatus.Rejected, true)]
    [InlineData(SampleStatus.Validated, SampleStatus.InProgress, false)]
    [InlineData(SampleStatus.Rejected, SampleStatus.Completed, false)]
    [InlineData(SampleStatus.Cancelled, SampleStatus.Registered, false)]
    public void IsTransitionAllowed_FollowsWorkflowRules(string from, string to, bool expected)
    {
        Assert.Equal(expected, DomainValidators.IsTransitionAllowed(from, to));
    }

    // ---- Specification evaluation (mirrors usp_SubmitResult) -------------------

    // Note: xUnit InlineData cannot carry decimal arguments (they arrive as
    // double at runtime), so the theory takes doubles and converts to decimal.
    [Theory]
    [InlineData(7.0, 6.5, 7.5, true)]     // inside range
    [InlineData(6.5, 6.5, 7.5, true)]     // on lower limit (inclusive)
    [InlineData(7.5, 6.5, 7.5, true)]     // on upper limit (inclusive)
    [InlineData(6.4, 6.5, 7.5, false)]    // below range
    [InlineData(7.6, 6.5, 7.5, false)]    // above range
    [InlineData(0.2, null, 0.5, true)]    // no lower limit
    [InlineData(0.6, null, 0.5, false)]
    [InlineData(50.0, 10.0, null, true)]  // no upper limit
    public void IsWithinSpec_EvaluatesLimitsCorrectly(double value, double? min, double? max, bool expected)
    {
        decimal ToDecimal(double d) => Convert.ToDecimal(d);
        Assert.Equal(expected, DomainValidators.IsWithinSpec(
            ToDecimal(value),
            min.HasValue ? ToDecimal(min.Value) : null,
            max.HasValue ? ToDecimal(max.Value) : null));
    }

    // ---------------------------------------------------------------- users

    [Fact]
    public void ValidatePassword_accepts_a_strong_password()
    {
        Assert.Empty(DomainValidators.ValidatePassword("Analyst@2026"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("alllowercase1")]
    [InlineData("ALLUPPERCASE1")]
    [InlineData("NoDigitsHere!")]
    public void ValidatePassword_rejects_weak_passwords(string? password)
    {
        Assert.NotEmpty(DomainValidators.ValidatePassword(password));
    }

    [Fact]
    public void ValidateNewUser_rejects_unknown_role()
    {
        var errors = DomainValidators.ValidateNewUser("new.user", "New User", "Admin");
        Assert.Contains(errors, e => e.Contains("Role"));
    }

    [Fact]
    public void ValidateNewUser_accepts_valid_analyst()
    {
        Assert.Empty(DomainValidators.ValidateNewUser("new.analyst", "New Analyst", UserRoles.Analyst));
    }

    // ── Username whitelist fix tests ──────────────────────────────────────────
    // These previously passed through the broken && condition:

    [Theory]
    [InlineData("user@name")]   // @ sign - not in whitelist
    [InlineData("user-name")]   // hyphen - not in whitelist
    [InlineData("user name")]   // space - not in whitelist
    [InlineData("user#1")]      // hash - not in whitelist
    public void ValidateNewUser_rejects_invalid_username_chars(string username)
    {
        var errors = DomainValidators.ValidateNewUser(username, "Display Name", UserRoles.Analyst);
        Assert.Contains(errors, e => e.Contains("letters") || e.Contains("digits") || e.Contains("Username"));
    }

    [Theory]
    [InlineData("analyst1")]        // letters + digit
    [InlineData("qual.manager")]    // with dot
    [InlineData("lab_user_01")]     // with underscores
    public void ValidateNewUser_accepts_valid_username_chars(string username)
    {
        var errors = DomainValidators.ValidateNewUser(username, "Display Name", UserRoles.Analyst);
        Assert.DoesNotContain(errors, e => e.Contains("letters") || e.Contains("Username"));
    }

    // ── Password max-length test ──────────────────────────────────────────────

    [Fact]
    public void ValidatePassword_rejects_excessively_long_password()
    {
        var tooLong = new string('A', 150) + new string('a', 50) + "1"; // 201 chars
        Assert.Contains(DomainValidators.ValidatePassword(tooLong), e => e.Contains("exceed"));
    }

    [Fact]
    public void ValidatePassword_accepts_password_at_max_length()
    {
        // Exactly 200 chars with upper, lower, digit
        var maxLen = "Aa1" + new string('x', 197);
        Assert.Empty(DomainValidators.ValidatePassword(maxLen));
    }
}