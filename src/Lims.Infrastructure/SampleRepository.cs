using Dapper;
using Lims.Core.Interfaces;
using Lims.Core.Models;
using Microsoft.Data.SqlClient;

namespace Lims.Infrastructure;

/// <summary>
/// SQL Server implementation of <see cref="ISampleRepository"/>.
/// All data access is routed through stored procedures — no inline SQL.
/// </summary>
public class SampleRepository : ISampleRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public SampleRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<(string SampleCode, int SampleId)> CreateSampleAsync(
        CreateSampleRequest request, string createdBy, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();

        var parameters = new DynamicParameters();
        parameters.Add("@SampleCode", dbType: System.Data.DbType.String, size: 30, direction: System.Data.ParameterDirection.Output);
        parameters.Add("@Description", request.Description);
        parameters.Add("@Matrix", request.Matrix);
        parameters.Add("@Priority", request.Priority);
        parameters.Add("@ClientCode", request.ClientCode);
        parameters.Add("@TestCodes", string.Join(",", request.TestCodes));
        parameters.Add("@CreatedBy", createdBy);

        var row = await conn.QuerySingleAsync(
            new CommandDefinition("dbo.usp_CreateSample", parameters,
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));

        return (parameters.Get<string>("@SampleCode")!, (int)row.SampleId);
    }

    public async Task<Sample?> GetSampleByCodeAsync(string sampleCode, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();

        using var multi = await conn.QueryMultipleAsync(
            new CommandDefinition("dbo.usp_GetSampleByCode",
                new { SampleCode = sampleCode },
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));

        var sample = await multi.ReadFirstOrDefaultAsync<Sample>();
        if (sample is null)
            return null;

        sample.Tests = (await multi.ReadAsync<SampleTest>()).ToList();
        return sample;
    }

    public async Task<PagedResult<Sample>> SearchSamplesAsync(SampleSearchFilter filter, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();

        using var multi = await conn.QueryMultipleAsync(
            new CommandDefinition("dbo.usp_SearchSamples",
                new
                {
                    filter.SearchText,
                    filter.Status,
                    filter.ClientCode,
                    PageNumber = filter.PageNumber,
                    PageSize = filter.PageSize
                },
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));

        var items = (await multi.ReadAsync<Sample>()).ToList();
        var total = await multi.ReadFirstAsync<int>();

        return new PagedResult<Sample>
        {
            Items = items,
            TotalCount = total,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }

    public async Task<ResultSubmissionResult> SubmitResultAsync(ResultSubmission submission, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();

        var row = await conn.QuerySingleAsync(
            new CommandDefinition("dbo.usp_SubmitResult",
                new
                {
                    submission.SampleCode,
                    submission.TestCode,
                    submission.ResultValue,
                    submission.InstrumentCode,
                    submission.Comment,
                    submission.Source
                },
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));

        return new ResultSubmissionResult { Passed = (bool)row.Passed, SampleStatus = (string)row.SampleStatus };
    }

    public async Task ChangeSampleStatusAsync(string sampleCode, string newStatus, string? comment,
        string changedBy, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.ExecuteAsync(
            new CommandDefinition("dbo.usp_ChangeSampleStatus",
                new { SampleCode = sampleCode, NewStatus = newStatus, Comment = comment, ChangedBy = changedBy },
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));
    }

    public async Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();

        using var multi = await conn.QueryMultipleAsync(
            new CommandDefinition("dbo.usp_GetDashboardStats",
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));

        var stats = new DashboardStats
        {
            SamplesByStatus = (await multi.ReadAsync<DashboardStats.StatusCount>()).ToList(),
            OverdueCalibrations = (await multi.ReadAsync<Instrument>()).ToList(),
            OutOfSpecTests = (await multi.ReadAsync<DashboardStats.OutOfSpecStat>()).ToList()
        };
        return stats;
    }

    public async Task<IReadOnlyList<Instrument>> GetInstrumentsAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        var rows = await conn.QueryAsync<Instrument>(
            new CommandDefinition("dbo.usp_GetInstruments",
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task LogAuditAsync(string source, string action, string? entityRef, bool isSuccess,
        string? message, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.ExecuteAsync(
            new CommandDefinition("dbo.usp_LogAudit",
                new { Source = source, Action = action, EntityRef = entityRef, IsSuccess = isSuccess, Message = message },
                commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));
    }
}