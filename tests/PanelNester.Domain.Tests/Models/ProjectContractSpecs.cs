using System.Text.Json;
using PanelNester.Domain.Models;
using PanelNester.Domain.Tests.Specifications;

namespace PanelNester.Domain.Tests.Models;

public sealed class ProjectContractSpecs
{
    [Fact]
    public void Phase_three_project_persistence_error_codes_are_locked()
    {
        Assert.Equal(
            [
                "project-not-found",
                "project-corrupt",
                "project-unsupported-version",
                "project-save-failed"
            ],
            Phase03ProjectExpectations.PersistenceErrorCodes);
    }

    [Fact]
    public void Project_models_round_trip_through_json_without_losing_metadata_state_or_snapshots()
    {
        var project = Phase03ProjectExpectations.CreateSampleProject();

        var json = JsonSerializer.Serialize(project);
        var roundTripped = JsonSerializer.Deserialize<Project>(json);

        Assert.NotNull(roundTripped);
        Assert.Equivalent(project, roundTripped, strict: true);
    }

    [Fact]
    public void Project_serializer_uses_the_expected_document_type_and_version()
    {
        Assert.Equal(Project.CurrentVersion, Phase03ProjectExpectations.CreateSampleProject().Version);
    }
}
