using System.Diagnostics;
using System.Runtime.Versioning;
using Lims.Core.Interfaces;
using Lims.Core.Services;
using Microsoft.Extensions.Options;

namespace Lims.WindowsService;

/// <summary>
/// LIMS instrument middleware.
/// Polls an "incoming" folder for CSV result files exported by lab analysers,
/// parses them, submits each result to the LIMS database (stored procedures)
/// and archives the file. Invalid lines are reported to the audit log and the
/// file is moved to an "error" folder for operator review.
///
/// When a file is quarantined to the error folder an <b>Application event log</b>
/// Warning entry is written (source: LimsInstrumentImport) so that operations
/// teams are alerted without needing to tail log files.
///
/// Runs as a Windows Service in production, as a console app in development.
/// </summary>
public class InstrumentImportWorker : BackgroundService
{
    private const string EventSourceName = "LimsInstrumentImport";
    private const int    ErrorEventId    = 1001;

    private readonly ISampleRepository  _repository;
    private readonly IAuditRepository   _audit;
    private readonly ILogger<InstrumentImportWorker> _logger;
    private readonly InstrumentImportOptions _options;

    public InstrumentImportWorker(
        ISampleRepository repository,
        IAuditRepository audit,
        IOptions<InstrumentImportOptions> options,
        ILogger<InstrumentImportWorker> logger)
    {
        _repository = repository;
        _audit      = audit;
        _options    = options.Value;
        _logger     = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Instrument import worker started. Watching {Folder}", _options.IncomingFolder);

        Directory.CreateDirectory(_options.IncomingFolder);
        Directory.CreateDirectory(_options.ArchiveFolder);
        Directory.CreateDirectory(_options.ErrorFolder);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessPendingFilesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let one bad file kill the service â€” log and keep polling.
                _logger.LogError(ex, "Unexpected error during import cycle");
            }
        }

        _logger.LogInformation("Instrument import worker stopped.");
    }

    private async Task ProcessPendingFilesAsync(CancellationToken ct)
    {
        var files = Directory.GetFiles(_options.IncomingFolder, _options.FilePattern);

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(filePath);
            _logger.LogInformation("Processing instrument file {File}", fileName);

            try
            {
                var content     = await File.ReadAllTextAsync(filePath, ct);
                var parsedLines = InstrumentFileParser.Parse(content, fileName);

                var ok     = 0;
                var failed = 0;

                foreach (var line in parsedLines)
                {
                    if (!line.IsValid || line.Submission is null)
                    {
                        failed++;
                        _logger.LogWarning("Line {Line} in {File} rejected: {Error}",
                            line.LineNumber, fileName, line.Error);
                        await _audit.LogAsync("WIN_SERVICE", "IMPORT_LINE_REJECTED",
                            line.Submission?.SampleCode ?? fileName, false, line.Error, ct);
                        continue;
                    }

                    try
                    {
                        var result = await _repository.SubmitResultAsync(line.Submission, ct);
                        ok++;
                        _logger.LogInformation(
                            "Result {Sample}/{Test} = {Value} -> {Outcome} (sample now {Status})",
                            line.Submission.SampleCode, line.Submission.TestCode,
                            line.Submission.ResultValue,
                            result.Passed ? "PASS" : "OUT-OF-SPEC", result.SampleStatus);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogError(ex, "DB error importing line {Line} of {File}",
                            line.LineNumber, fileName);
                        await _audit.LogAsync("WIN_SERVICE", "IMPORT_LINE_ERROR",
                            line.Submission.SampleCode, false, ex.Message, ct);
                    }
                }

                await _audit.LogAsync("WIN_SERVICE", "IMPORT_FILE_DONE", fileName,
                    failed == 0, $"OK={ok}, Failed={failed}", ct);

                // Archive or quarantine the file after processing.
                var toError      = failed > 0;
                var targetFolder = toError ? _options.ErrorFolder : _options.ArchiveFolder;
                var targetPath   = Path.Combine(targetFolder, $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{fileName}");
                File.Move(filePath, targetPath, overwrite: true);

                _logger.LogInformation("File {File} moved to {Folder} (OK={Ok}, Failed={Failed})",
                    fileName, targetFolder, ok, failed);

                // ── Windows Event Log alert ─────────────────────────────────
                // Write a Warning to the Application log so operators receive a
                // visible alert without needing to monitor log files.
                if (toError && _options.AlertOnFileError && OperatingSystem.IsWindows())
                    RaiseEventLogWarning(fileName, ok, failed);
            }
            catch (IOException ex)
            {
                // File still being written by the instrument â€” retry next cycle.
                _logger.LogWarning(ex, "File {File} is locked, will retry next cycle", fileName);
            }
        }
    }

    /// <summary>
    /// Writes a Warning to the Windows Application event log (source: LimsInstrumentImport).
    /// Requires the event source to be pre-registered in the registry, which the Windows Service
    /// installer should do with <c>New-EventLog -LogName Application -Source LimsInstrumentImport</c>.
    /// Silently swallows any SecurityException (non-admin dev machines).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private void RaiseEventLogWarning(string fileName, int ok, int failed)
    {
        try
        {
            if (!EventLog.SourceExists(EventSourceName))
                EventLog.CreateEventSource(EventSourceName, "Application");

            using var log = new EventLog("Application") { Source = EventSourceName };
            log.WriteEntry(
                $"LIMS instrument file quarantined: {fileName}\r\n" +
                $"OK={ok} rows, Failed={failed} rows.\r\n" +
                $"Review file in: {_options.ErrorFolder}",
                EventLogEntryType.Warning,
                ErrorEventId);

            _logger.LogWarning("Windows Event Log warning written for file {File}", fileName);
        }
        catch (Exception ex)
        {
            // Non-fatal: log the failure but don't rethrow.
            _logger.LogWarning(ex, "Could not write Windows Event Log entry for {File}", fileName);
        }
    }
}

/// <summary>Configuration bound to the "InstrumentImport" section of appsettings.json.</summary>
public class InstrumentImportOptions
{
    public string IncomingFolder      { get; set; } = @"C:\Lims\InstrumentData\incoming";
    public string ArchiveFolder       { get; set; } = @"C:\Lims\InstrumentData\archive";
    public string ErrorFolder         { get; set; } = @"C:\Lims\InstrumentData\error";
    public string FilePattern         { get; set; } = "*.csv";
    public int    PollIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// When <c>true</c>, a Windows Application Event Log Warning is raised whenever a file
    /// is moved to the error folder. Set to <c>false</c> in development to avoid needing
    /// elevated privileges for event source registration.
    /// </summary>
    public bool AlertOnFileError { get; set; } = true;
}

