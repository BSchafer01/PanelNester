using System.Text.Json;
using PanelNester.Domain.Models;
using PanelNester.Domain.Tests.Specifications;

namespace PanelNester.Domain.Tests.Models;

public sealed class MaterialContractSpecs
{
    [Fact]
    public void Phase_two_material_error_codes_are_locked_for_crud_and_import_feedback()
    {
        Assert.Equal(
            ["material-not-found", "material-name-exists", "material-in-use"],
            Phase02MaterialExpectations.CrudErrorCodes);
    }

    [Fact]
    public void Materials_round_trip_through_json_persistence_without_losing_optional_fields()
    {
        var material = Phase02MaterialExpectations.CreateSampleMaterial();

        var json = JsonSerializer.Serialize(material);
        var roundTripped = JsonSerializer.Deserialize<Material>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(material, roundTripped);
    }

    [Fact]
    public void Demo_material_catalog_stays_unique_when_used_as_the_phase_two_seed_library()
    {
        Assert.True(Phase02MaterialExpectations.HasUniqueNames(DemoMaterialCatalog.All));

        var seed = Assert.Single(DemoMaterialCatalog.All);
        Assert.Equal("Demo Material", seed.Name);
        Assert.True(seed.AllowRotation);
    }
}
