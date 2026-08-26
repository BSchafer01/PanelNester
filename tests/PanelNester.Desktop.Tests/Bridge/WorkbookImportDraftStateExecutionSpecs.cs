using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class WorkbookImportDraftStateExecutionSpecs
{
    [Fact]
    public void Workbook_drafts_select_only_the_first_Worksheet_and_restore_reselected_configuration()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var fixturePath = Path.Combine(
            repositoryRoot,
            "tests",
            "PanelNester.Desktop.Tests",
            "Bridge",
            "WorkbookImportDraftStateFixture.cjs");
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
        var output = process!.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);

        var result = JsonSerializer.Deserialize<Result>(
            output,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(result);
        Assert.Equal([true, false, false], result!.InitialSelection);
        Assert.Equal([1, 2, 3], result.DefaultGroups.Select(group => group.Position));
        Assert.Equal(["First", "Second", "Third"], result.DefaultGroups.Select(group => group.Name));
        Assert.True(result.RestoredDraft.Selected);
        Assert.Equal("combined", result.RestoredDraft.OptimizationGroupId);
        Assert.Equal(["Id", "Length"], result.RestoredDraft.MappedFields);
    }

    private sealed record Result(
        bool[] InitialSelection,
        DefaultGroup[] DefaultGroups,
        RestoredDraft RestoredDraft);

    private sealed record DefaultGroup(string Name, int Position);

    private sealed record RestoredDraft(
        bool Selected,
        string OptimizationGroupId,
        string[] MappedFields);
}
