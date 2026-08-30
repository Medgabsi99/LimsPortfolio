using System.Globalization;
using Lims.Core.Models;

namespace Lims.Core.Services;

/// <summary>
/// Parses raw CSV result files produced by laboratory instruments.
/// Expected format (one line per measurement):
///   SampleCode,TestCode,InstrumentCode,ResultValue,MeasuredAt(yyyy-MM-dd HH:mm:ss)
/// Lines starting with '#' are comments; empty lines are ignored.
/// Pure, side-effect free -> fully unit-testable.
/// </summary>
public static class InstrumentFileParser
{
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    public static IReadOnlyList<ParsedLine> Parse(string fileContent, string sourceFileName)
    {
        var results = new List<ParsedLine>();
        if (string.IsNullOrWhiteSpace(fileContent))
            return results;

        var lines = fileContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].Trim();
            if (raw.Length == 0 || raw.StartsWith("#"))
                continue;

            var columns = raw.Split(',');
            if (columns.Length < 4)
            {
                results.Add(ParsedLine.Invalid(sourceFileName, i + 1, raw, "Expected at least 4 columns."));
                continue;
            }

            var sampleCode = columns[0].Trim();
            var testCode = columns[1].Trim().ToUpperInvariant();
            var instrumentCode = columns[2].Trim().ToUpperInvariant();
            var valueText = columns[3].Trim();

            if (!decimal.TryParse(valueText, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                results.Add(ParsedLine.Invalid(sourceFileName, i + 1, raw, $"Invalid numeric value '{valueText}'."));
                continue;
            }

            DateTime? measuredAt = null;
            if (columns.Length >= 5 &&
                DateTime.TryParseExact(columns[4].Trim(), DateTimeFormat, CultureInfo.InvariantCulture,
                                       DateTimeStyles.AssumeUniversal, out var parsedDate))
            {
                measuredAt = parsedDate;
            }

            results.Add(new ParsedLine
            {
                IsValid = true,
                SourceFile = sourceFileName,
                LineNumber = i + 1,
                Submission = new ResultSubmission
                {
                    SampleCode = sampleCode,
                    TestCode = testCode,
                    InstrumentCode = string.IsNullOrEmpty(instrumentCode) ? null : instrumentCode,
                    ResultValue = value,
                    Source = "WIN_SERVICE",
                    MeasuredAt = measuredAt
                }
            });
        }

        return results;
    }

    public class ParsedLine
    {
        public bool IsValid { get; init; }
        public string SourceFile { get; init; } = string.Empty;
        public int LineNumber { get; init; }
        public string? Error { get; init; }
        public string? RawLine { get; init; }
        public ResultSubmission? Submission { get; init; }

        public static ParsedLine Invalid(string file, int line, string raw, string error) =>
            new() { IsValid = false, SourceFile = file, LineNumber = line, RawLine = raw, Error = error };
    }
}