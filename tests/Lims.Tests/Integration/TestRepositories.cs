using Lims.Core.Interfaces;
using Lims.Core.Models;
using Lims.Core.Services;

namespace Lims.Tests.Integration;

/// <summary>
/// In-memory user store replacing SQL Server in integration tests.
/// Seeds the same default accounts as database/04_seed_data.sql, with hashes
/// produced by the real PBKDF2 PasswordHasher.
/// </summary>
public sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<int, UserAccount> _byId = new();
    private readonly Dictionary<string, UserAccount> _byName = new(StringComparer.OrdinalIgnoreCase);
    private int _nextId;

    public FakeUserRepository(IPasswordHasher hasher)
    {
        Add(hasher, "analyst1", "Lab Analyst 1", UserRoles.Analyst, "Analyst@2026");
        Add(hasher, "qual.manager", "Quality Manager", UserRoles.Manager, "Manager@2026");
    }

    private void Add(IPasswordHasher hasher, string username, string displayName, string role, string password)
    {
        var account = new UserAccount
        {
            UserId = ++_nextId,
            Username = username,
            DisplayName = displayName,
            Role = role,
            IsActive = true,
            TokenVersion = 1
        };
        account.PasswordSalt = hasher.NewSalt();
        account.PasswordHash = hasher.Hash(password, account.PasswordSalt);
        _byId[account.UserId] = account;
        _byName[account.Username] = account;
    }

    public Task<UserAccount?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
        Task.FromResult(_byName.GetValueOrDefault(username));

    public Task<UserAccount?> GetByIdAsync(int userId, CancellationToken ct = default) =>
        Task.FromResult(_byId.GetValueOrDefault(userId));

    public Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UserAccount>>(_byId.Values.OrderBy(u => u.Username).ToList());

    public Task<int> CreateAsync(UserAccount account, string createdBy, CancellationToken ct = default)
    {
        account.UserId = ++_nextId;
        _byId[account.UserId] = account;
        _byName[account.Username] = account;
        return Task.FromResult(account.UserId);
    }

    public Task<bool> SetActiveAsync(int userId, bool isActive, string changedBy, CancellationToken ct = default)
    {
        if (!_byId.TryGetValue(userId, out var account)) return Task.FromResult(false);
        account.IsActive = isActive;
        account.TokenVersion++;
        return Task.FromResult(true);
    }

    public Task<bool> ChangePasswordAsync(int userId, string newHash, string newSalt, string changedBy, CancellationToken ct = default) =>
        ApplyPassword(userId, newHash, newSalt);

    public Task<bool> ResetPasswordAsync(int userId, string newHash, string newSalt, string changedBy, CancellationToken ct = default) =>
        ApplyPassword(userId, newHash, newSalt);

    private Task<bool> ApplyPassword(int userId, string hash, string salt)
    {
        if (!_byId.TryGetValue(userId, out var account)) return Task.FromResult(false);
        account.PasswordHash = hash;
        account.PasswordSalt = salt;
        account.TokenVersion++;
        return Task.FromResult(true);
    }
}

/// <summary>In-memory sample store replacing SQL Server in integration tests.</summary>
public sealed class FakeSampleRepository : ISampleRepository
{
    private readonly Dictionary<string, Sample> _samples = new(StringComparer.OrdinalIgnoreCase);

    public FakeSampleRepository()
    {
        _samples["SMP-2026-00001"] = new Sample
        {
            SampleId = 1,
            SampleCode = "SMP-2026-00001",
            ClientCode = "CLI-001",
            ClientName = "Pharma BV",
            Status = SampleStatus.Completed   // -> VALIDATED is the happy-path manager transition
        };
    }

    public Task<(string SampleCode, int SampleId)> CreateSampleAsync(CreateSampleRequest request, string createdBy, CancellationToken ct = default) =>
        Task.FromResult(("SMP-2026-00999", 999));

    public Task<Sample?> GetSampleByCodeAsync(string sampleCode, CancellationToken ct = default) =>
        Task.FromResult(_samples.GetValueOrDefault(sampleCode));

    public Task<PagedResult<Sample>> SearchSamplesAsync(SampleSearchFilter filter, CancellationToken ct = default) =>
        Task.FromResult(new PagedResult<Sample> { Items = _samples.Values.ToList(), TotalCount = _samples.Count, PageNumber = filter.PageNumber, PageSize = filter.PageSize });

    public Task<ResultSubmissionResult> SubmitResultAsync(ResultSubmission submission, CancellationToken ct = default) =>
        Task.FromResult(new ResultSubmissionResult { Passed = true, SampleStatus = SampleStatus.InProgress });

    public Task ChangeSampleStatusAsync(string sampleCode, string newStatus, string? comment, string changedBy, CancellationToken ct = default)
    {
        if (_samples.TryGetValue(sampleCode, out var sample))
            sample.Status = newStatus;
        return Task.CompletedTask;
    }

    public Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct = default) =>
        Task.FromResult(new DashboardStats());

    public Task<IReadOnlyList<Instrument>> GetInstrumentsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Instrument>>(Array.Empty<Instrument>());

    public Task LogAuditAsync(string source, string action, string? entityRef, bool isSuccess, string? message, CancellationToken ct = default) =>
        Task.CompletedTask;
}

/// <summary>
/// No-op audit sink for integration tests. AuthController writes audit entries;
/// this keeps tests self-contained without a DB.
/// </summary>
public sealed class FakeAuditRepository : IAuditRepository
{
    public Task LogAsync(string source, string action, string? entityRef,
        bool isSuccess, string? message, CancellationToken ct = default)
        => Task.CompletedTask;
}