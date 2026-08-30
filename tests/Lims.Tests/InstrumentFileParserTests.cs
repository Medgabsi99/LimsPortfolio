using Lims.Core.Services;
using Xunit;

namespace Lims.Tests;

/// <summary>Unit tests for the instrument CSV file parser (middleware core logic).</summary>
public class InstrumentFileParserTests
{
    [Fact]
    public void Parse_WithValidLines_ReturnsSubmissions()
    {
        var content = "# instrument export\n" +
                      "SMP-2026-00001,PH,PHM-01,7.12,2026-08-28 10:15:00\n" +
                      "SMP-2026-00001,ASSAY,HPLC-01,99.4,2026-08-28 10:20:00\n";

        var lines = InstrumentFileParser.Parse(content, "results.csv");

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.True(l.IsValid));

        var first = lines[0].Submission!;
        Assert.Equal("SMP-2026-00001", first.SampleCode);
        Assert.Equal("PH", first.TestCode);
        Assert.Equal("PHM-01", first.InstrumentCode);
        Assert.Equal(7.12m, first.ResultValue);
        Assert.Equal("WIN_SERVICE", first.Source);
        Assert.NotNull(first.MeasuredAt);
    }

    [Fact]
    public void Parse_WithInvalidNumber_MarksLineInvalidWithError()
    {
        var content = "SMP-2026-00001,PH,PHM-01,NOT_A_NUMBER\n";

        var lines = InstrumentFileParser.Parse(content, "bad.csv");

        Assert.Single(lines);
        Assert.False(lines[0].IsValid);
        Assert.Contains("Invalid numeric value", lines[0].Error);
        Assert.Null(lines[0].Submission);
    }

    [Fact]
    public void Parse_WithTooFewColumns_MarksLineInvalid()
    {
        var content = "SMP-2026-00001,PH\n";

        var lines = InstrumentFileParser.Parse(content, "short.csv");

        Assert.Single(lines);
        Assert.False(lines[0].IsValid);
        Assert.Contains("4 columns", lines[0].Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# only a comment\n# another comment")]
    public void Parse_WithNoDataLines_ReturnsEmpty(string content)
    {
        var lines = InstrumentFileParser.Parse(content, "empty.csv");
        Assert.Empty(lines);
    }

    [Fact]
    public void Parse_NormalizesCodesToUpperCase()
    {
        var content = "SMP-2026-00001,ph,phm-01,7.0\n";

        var lines = InstrumentFileParser.Parse(content, "case.csv");

        Assert.Equal("PH", lines[0].Submission!.TestCode);
        Assert.Equal("PHM-01", lines[0].Submission!.InstrumentCode);
    }

    [Fact]
    public void Parse_WithoutTimestamp_LeavesMeasuredAtNull()
    {
        var content = "SMP-2026-00001,PH,PHM-01,7.0\n";

        var lines = InstrumentFileParser.Parse(content, "nots.csv");

        Assert.Null(lines[0].Submission!.MeasuredAt);
    }
}