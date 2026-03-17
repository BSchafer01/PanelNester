using PanelNester.Domain.Models;
using PanelNester.Services.Materials;

namespace PanelNester.Services.Tests.Materials;

public sealed class MaterialValidationServiceSpecs
{
    private readonly MaterialValidationService _service = new();

    [Fact]
    public void Create_validation_trims_optional_fields_and_accepts_valid_materials()
    {
        var result = _service.ValidateForCreate(
            new Material
            {
                MaterialId = "ignored-on-create",
                Name = "  Birch Ply  ",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m,
                ColorFinish = "  White  ",
                Notes = "   ",
                CostPerSheet = 72.50m
            },
            []);

        Assert.True(result.IsValid);
        Assert.Equal("Birch Ply", result.Material.Name);
        Assert.Equal("White", result.Material.ColorFinish);
        Assert.Null(result.Material.Notes);
    }

    [Fact]
    public void Create_validation_rejects_duplicate_names_case_insensitively()
    {
        var result = _service.ValidateForCreate(
            BuildMaterial("candidate", "birch ply"),
            [BuildMaterial("existing", "Birch Ply")]);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("material-name-exists", error.Code);
    }

    [Fact]
    public void Update_validation_allows_the_existing_record_to_keep_its_name()
    {
        var currentMaterial = BuildMaterial("mat-1", "Birch Ply");

        var result = _service.ValidateForUpdate(
            currentMaterial with { Notes = "Updated notes" },
            [currentMaterial]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal("Updated notes", result.Material.Notes);
    }

    [Fact]
    public void Validation_returns_actionable_error_codes_for_invalid_dimensions_and_costs()
    {
        var result = _service.ValidateForCreate(
            new Material
            {
                Name = "  ",
                SheetLength = 0m,
                SheetWidth = -1m,
                DefaultSpacing = -0.125m,
                DefaultEdgeMargin = -0.5m,
                CostPerSheet = -10m
            },
            []);

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                "material-name-required",
                "sheet-length-out-of-range",
                "sheet-width-out-of-range",
                "default-spacing-out-of-range",
                "default-edge-margin-out-of-range",
                "cost-per-sheet-out-of-range"
            ],
            result.Errors.Select(error => error.Code).ToArray());
    }

    private static Material BuildMaterial(string materialId, string name) =>
        new()
        {
            MaterialId = materialId,
            Name = name,
            SheetLength = 96m,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = 0.125m,
            DefaultEdgeMargin = 0.5m
        };
}
