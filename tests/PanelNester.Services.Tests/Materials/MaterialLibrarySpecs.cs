using PanelNester.Domain.Models;
using PanelNester.Services.Materials;
using PanelNester.Services.Tests.Specifications;

namespace PanelNester.Services.Tests.Materials;

public sealed class MaterialLibrarySpecs
{
    [Fact]
    public void Duplicate_material_names_are_rejected_with_material_name_exists()
    {
        var error = Phase02MaterialLibrarySpec.ClassifyNameConflict(
            ["Demo Material", "Baltic Birch"],
            "baltic birch");

        Assert.Equal("material-name-exists", error);
    }

    [Fact]
    public void Renaming_a_material_to_its_current_name_is_not_a_false_duplicate()
    {
        var error = Phase02MaterialLibrarySpec.ClassifyNameConflict(
            ["Demo Material", "Baltic Birch"],
            "baltic birch",
            currentName: "Baltic Birch");

        Assert.Null(error);
    }

    [Fact]
    public void In_use_materials_are_protected_from_deletion()
    {
        Assert.Equal("material-in-use", Phase02MaterialLibrarySpec.ClassifyDeleteBlocker(materialInUse: true));
        Assert.Null(Phase02MaterialLibrarySpec.ClassifyDeleteBlocker(materialInUse: false));
    }

    [Fact]
    public void Import_selection_requires_an_exact_match_against_selected_project_materials()
    {
        string[] selectedMaterials = ["Baltic Birch", "Black ACM"];

        Assert.Null(Phase02MaterialLibrarySpec.ClassifyImportSelection(selectedMaterials, "Black ACM"));
        Assert.Equal("material-not-found", Phase02MaterialLibrarySpec.ClassifyImportSelection(selectedMaterials, "black acm"));
        Assert.Equal("material-not-found", Phase02MaterialLibrarySpec.ClassifyImportSelection(selectedMaterials, "Demo Material"));
    }

    [Fact]
    public async Task Material_crud_operations_persist_through_a_json_round_trip()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.MaterialLibrarySpecs.{Guid.NewGuid():N}");

        try
        {
            var filePath = Path.Combine(workspacePath, "materials.json");
            var repository = new JsonMaterialRepository(filePath);
            var service = new MaterialService(repository, idGenerator: () => "black-acm");

            var createResult = await service.CreateAsync(
                new Material
                {
                    Name = "Black ACM",
                    SheetLength = 120m,
                    SheetWidth = 60m,
                    AllowRotation = true,
                    DefaultSpacing = 0.125m,
                    DefaultEdgeMargin = 0.5m
                });

            Assert.True(createResult.Success);
            Assert.NotNull(createResult.Material);
            Assert.Equal("black-acm", createResult.Material!.MaterialId);

            var getResult = await service.GetAsync(createResult.Material.MaterialId);
            Assert.True(getResult.Success);
            Assert.Equal("Black ACM", getResult.Material!.Name);

            var updateResult = await service.UpdateAsync(createResult.Material with { Notes = "Exterior panels" });
            Assert.True(updateResult.Success);
            Assert.Equal("Exterior panels", updateResult.Material!.Notes);

            var deleteResult = await service.DeleteAsync(createResult.Material.MaterialId);
            Assert.True(deleteResult.Success);

            var materials = await repository.GetAllAsync();
            Assert.DoesNotContain(materials, material => material.MaterialId == createResult.Material.MaterialId);
        }
        finally
        {
            if (Directory.Exists(workspacePath))
            {
                Directory.Delete(workspacePath, true);
            }
        }
    }

    [Fact(Skip = "Blocked until the import flow can select project materials from the Phase 2 library.")]
    public void Import_flow_uses_selected_project_materials_instead_of_the_phase_one_demo_material()
    {
    }
}
