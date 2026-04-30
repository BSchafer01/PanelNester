using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class ResultsBatchSheetSearchExecutionSpecs
{
    [Fact]
    public void Results_batch_sheet_search_returns_only_the_exact_rows_from_the_reported_live_examples()
    {
        var output = RunNodeFixture("tests", "PanelNester.Desktop.Tests", "Bridge", "ResultsBatchSheetSearchFixture.cjs");
        var payload = JsonSerializer.Deserialize<SearchExecutionPayload>(
            output,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(payload);
        Assert.True(payload!.DirectChecks.ExactHit);
        Assert.True(payload.DirectChecks.SeparatorHit);
        Assert.False(payload.DirectChecks.FalsePositive0408);
        Assert.False(payload.DirectChecks.FalsePositive0407);
        Assert.False(payload.DirectChecks.FalsePositive00004);
        Assert.False(payload.DirectChecks.FalsePositive00040);
        Assert.False(payload.DirectChecks.FalsePositive00045);

        Assert.Equal(7, payload.TotalMatchCount);
        Assert.Equal(3, payload.MatchedSheetCount);
        Assert.Equal(
            [
                new SearchMatchRow("panel-04-013", "Mat-ACM-62x196", 21),
                new SearchMatchRow("PANEL-04013-LEFT", "Mat-Birch-48x96", 34),
                new SearchMatchRow("PANEL-04013-RIGHT", "Mat-Birch-48x96", 34),
                new SearchMatchRow("PANEL-04013#1", "Mat-Steel-48x120", 14),
                new SearchMatchRow("PANEL-04013#2", "Mat-Steel-48x120", 14),
                new SearchMatchRow("PANEL-04013#3", "Mat-Steel-48x120", 14),
                new SearchMatchRow("XX-04013-ZZ", "Mat-ACM-62x196", 21)
            ],
            payload.Matches);

        Assert.DoesNotContain(payload.Matches, match => match.PartId == "PANEL-00004#2");
        Assert.DoesNotContain(payload.Matches, match => match.PartId == "PANEL-0408#2");
        Assert.DoesNotContain(payload.Matches, match => match.PartId == "PANEL-0407#3");
        Assert.DoesNotContain(payload.Matches, match => match.PartId == "PANEL-00040#1");
        Assert.DoesNotContain(payload.Matches, match => match.PartId == "PANEL-00040#2");
        Assert.DoesNotContain(payload.Matches, match => match.PartId == "PANEL-00045#1");
        Assert.DoesNotContain(payload.Matches, match => match.PartId == "PANEL-00045#2");
        Assert.DoesNotContain(payload.Matches, match => match.PartId == "PANEL-00045#3");
    }

    [Fact]
    public void Results_batch_sheet_search_summary_rows_and_sheet_highlights_share_the_same_filtered_result_source()
    {
        var output = RunNodeFixture("tests", "PanelNester.Desktop.Tests", "Bridge", "ResultsBatchSheetSearchFixture.cjs");
        var payload = JsonSerializer.Deserialize<SearchExecutionPayload>(
            output,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(payload);
        Assert.Equal(payload!.TotalMatchCount, payload.SheetCounts.Sum(entry => entry.Count));
        Assert.Equal(payload.MatchedSheetCount, payload.SheetCounts.Length);
        Assert.Equal(payload.MatchedSheetCount, payload.FirstMatchesBySheet.Length);
        Assert.Equal(
            ["mat-acm-hit:sheet-021", "mat-birch:sheet-034", "mat-steel:sheet-014"],
            payload.SheetCounts.Select(entry => entry.SheetKey).ToArray());
        Assert.Equal(
            ["panel-04-013", "PANEL-04013-LEFT", "PANEL-04013#1"],
            payload.FirstMatchesBySheet.Select(entry => entry.PartId).ToArray());
    }

    private static string RunNodeFixture(params string[] relativePathSegments)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var pathSegments = new List<string> { repositoryRoot };
        pathSegments.AddRange(relativePathSegments);
        var fixturePath = Path.Combine(pathSegments.ToArray());
        var startInfo = new ProcessStartInfo("node")
        {
            Arguments = $"\"{fixturePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repositoryRoot
        };

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var standardOutput = process!.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"Node fixture failed with exit code {process.ExitCode}.{Environment.NewLine}{standardError}");

        return standardOutput;
    }

    private sealed record SearchExecutionPayload(
        SearchMatchRow[] Matches,
        int TotalMatchCount,
        int MatchedSheetCount,
        SearchSheetCount[] SheetCounts,
        SearchFirstMatch[] FirstMatchesBySheet,
        SearchDirectChecks DirectChecks);

    private sealed record SearchMatchRow(string PartId, string MaterialName, int SheetNumber);

    private sealed record SearchSheetCount(string SheetKey, int Count);

    private sealed record SearchFirstMatch(string SheetKey, string PartId);

    private sealed record SearchDirectChecks(
        bool ExactHit,
        bool SeparatorHit,
        bool FalsePositive0408,
        bool FalsePositive0407,
        bool FalsePositive00004,
        bool FalsePositive00040,
        bool FalsePositive00045);
}
