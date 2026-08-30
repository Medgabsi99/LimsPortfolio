using Lims.Core.Models;

namespace Lims.Core.Services;

/// <summary>
/// Pure business rules of the LIMS workflow. Kept free of I/O so they can be
/// unit-tested and reused by the REST API, the SOAP service and the Windows Service.
/// </summary>
public static class DomainValidators
{
    /// <summary>Validates a sample creation request against lab business rules.</summary>
    public static IReadOnlyList<string> Validate(CreateSampleRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.ClientCode))
            errors.Add("Client code is required.");

        if (request.TestCodes is null || request.TestCodes.Count == 0)
            errors.Add("At least one test must be requested.");

        if (request.TestCodes is { Count: > 20 })
            errors.Add("A sample cannot request more than 20 tests.");

        if (request.Priority is < 1 or > 3)
            errors.Add("Priority must be 1 (high), 2 (normal) or 3 (low).");

        if (request.TestCodes is not null &&
            request.TestCodes.Select(t => t.Trim().ToUpperInvariant()).Distinct().Count() != request.TestCodes.Count)
            errors.Add("Duplicate test codes are not allowed.");

        return errors;
    }

    /// <summary>Validates a result submission before it reaches the database.</summary>
    public static IReadOnlyList<string> Validate(ResultSubmission submission)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(submission.SampleCode))
            errors.Add("Sample code is required.");

        if (string.IsNullOrWhiteSpace(submission.TestCode))
            errors.Add("Test code is required.");

        if (submission.ResultValue < 0)
            errors.Add("Result value cannot be negative.");

        return errors;
    }

    /// <summary>Checks whether a manual status transition is allowed by the workflow.</summary>
    public static bool IsTransitionAllowed(string from, string to) => SampleStatus.CanTransition(from, to);

    /// <summary>
    /// Evaluates a measured value against specification limits
    /// (same rule as dbo.usp_SubmitResult - defense in depth).
    /// </summary>
    public static bool IsWithinSpec(decimal value, decimal? minSpec, decimal? maxSpec) =>
        (minSpec is null || value >= minSpec) && (maxSpec is null || value <= maxSpec);

    // ------------------------------------------------------------------ users

    /// <summary>Minimum password length enforced on create / change / reset.</summary>
    public const int MinimumPasswordLength = 8;

    /// <summary>Maximum password length — prevents a PBKDF2 DoS via enormous inputs.</summary>
    public const int MaximumPasswordLength = 200;

    /// <summary>
    /// Password policy for account creation, self-service change and resets:
    /// 8–200 characters including an uppercase letter, a lowercase letter and a digit.
    /// Pure and unit-testable.
    /// </summary>
    public static IReadOnlyList<string> ValidatePassword(string? password)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(password))
        {
            errors.Add("Password is required.");
            return errors;
        }

        if (password.Length < MinimumPasswordLength)
            errors.Add($"Password must be at least {MinimumPasswordLength} characters long.");

        if (password.Length > MaximumPasswordLength)
            errors.Add($"Password must not exceed {MaximumPasswordLength} characters.");

        if (!password.Any(char.IsUpper))
            errors.Add("Password must contain an uppercase letter.");

        if (!password.Any(char.IsLower))
            errors.Add("Password must contain a lowercase letter.");

        if (!password.Any(char.IsDigit))
            errors.Add("Password must contain a digit.");

        return errors;
    }

    /// <summary>Validates a new lab account before hashing (username format, role, display name).</summary>
    /// <remarks>
    /// Allowed username characters: letters, digits, dots and underscores.
    /// Spaces and special symbols (e.g. @, #, -) are rejected to keep usernames
    /// safe for use in log messages and audit trails without escaping.
    /// </remarks>
    public static IReadOnlyList<string> ValidateNewUser(string? username, string? displayName, string? role)
    {
        var errors = new List<string>();

        var trimmed = username?.Trim() ?? string.Empty;

        if (trimmed.Length < 3 || trimmed.Length > 50)
            errors.Add("Username must be 3-50 characters.");

        // Whitelist: letters, digits, dots, underscores only.
        // The previous check used '&&' which inadvertently allowed usernames
        // such as "user@name" (non-alphanum but no space). Fixed to a proper
        // character-set whitelist so each violation is caught independently.
        if (trimmed.Length > 0 && !trimmed.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '_'))
            errors.Add("Username may only contain letters, digits, dots and underscores.");

        if (role != UserRoles.Analyst && role != UserRoles.Manager)
            errors.Add("Role must be 'Analyst' or 'Manager'.");

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 100)
            errors.Add("Display name is required (max 100 characters).");

        return errors;
    }
}