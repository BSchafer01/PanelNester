using System.Text.Json;
using PanelNester.Domain.Models;
using PanelNester.Domain.Tests.Specifications;

namespace PanelNester.Domain.Tests.Models;

public sealed class ProjectPersistenceContractSpecs
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Phase_three_project_error_codes_cover_save_open_and_version_failures()
    {
        Assert.Equal(
            ["project-not-found", "project-corrupt", "project-unsupported-version", "project-save-failed"],
            Phase03ProjectExpectations.PersistenceErrorCodes);
    }

    [Fact]
    public void Project_documents_round_trip_through_json_with_metadata_snapshots_and_last_nesting_result()
    {
        var project = Phase03ProjectExpectations.CreateSampleProject();

        var json = JsonSerializer.Serialize(project, SerializerOptions);
        var roundTripped = JsonSerializer.Deserialize<Project>(json, SerializerOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal(project.Version, roundTripped!.Version);
        Assert.Equal(project.ProjectId, roundTripped.ProjectId);
        Assert.Equal(project.Metadata, roundTripped.Metadata);
        Assert.Equal(project.Settings, roundTripped.Settings);
        Assert.Equal(project.MaterialSnapshots, roundTripped.MaterialSnapshots);
        Assert.Equal(project.State.SourceFilePath, roundTripped.State.SourceFilePath);
        Assert.Equivalent(project.State.Parts, roundTripped.State.Parts, strict: true);
        Assert.Equal(project.State.SelectedMaterialId, roundTripped.State.SelectedMaterialId);
        Assert.NotNull(roundTripped.State.LastNestingResult);
        Assert.Equivalent(project.State.LastNestingResult, roundTripped.State.LastNestingResult, strict: true);
        Assert.NotNull(roundTripped.State.LastBatchNestingResult);
        Assert.Equivalent(project.State.LastBatchNestingResult, roundTripped.State.LastBatchNestingResult, strict: true);
    }

    [Fact]
    public void Project_document_uses_the_phase_three_schema_keys_for_metadata_snapshots_and_results()
    {
        var project = Phase03ProjectExpectations.CreateSampleProject();
        var json = JsonSerializer.Serialize(project, SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        foreach (var propertyName in Phase03ProjectExpectations.RootPropertyNames)
        {
            Assert.True(root.TryGetProperty(propertyName, out _), $"Expected root property '{propertyName}'.");
        }

        var metadata = root.GetProperty("metadata");
        foreach (var propertyName in Phase03ProjectExpectations.MetadataPropertyNames)
        {
            Assert.True(metadata.TryGetProperty(propertyName, out _), $"Expected metadata property '{propertyName}'.");
        }

        var settings = root.GetProperty("settings");
        Assert.True(settings.TryGetProperty("reportSettings", out _), "Expected settings property 'reportSettings'.");

        var snapshots = root.GetProperty("materialSnapshots");
        Assert.Equal(JsonValueKind.Array, snapshots.ValueKind);
        Assert.Equal("Baltic Birch", snapshots[0].GetProperty("name").GetString());

        var state = root.GetProperty("state");
        foreach (var propertyName in Phase03ProjectExpectations.StatePropertyNames)
        {
            Assert.True(state.TryGetProperty(propertyName, out _), $"Expected state property '{propertyName}'.");
        }

        var lastNestingResult = state.GetProperty("lastNestingResult");
        Assert.Equal(1, lastNestingResult.GetProperty("summary").GetProperty("totalSheets").GetInt32());

        var lastBatchNestingResult = state.GetProperty("lastBatchNestingResult");
        Assert.Equal(
            1,
            lastBatchNestingResult.GetProperty("materialResults")[0]
                .GetProperty("result")
                .GetProperty("summary")
                .GetProperty("totalSheets")
                .GetInt32());
    }
}
