using System.Text.Json;
using PanelNester.Domain.Models;
using PanelNester.Services.Materials;

namespace PanelNester.Services.Tests.Persistence;

public sealed class JsonMaterialRepositorySpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.JsonMaterialRepositorySpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Missing_store_is_seeded_with_the_demo_material()
    {
        var filePath = Path.Combine(_workspacePath, "materials.json");
        var repository = new JsonMaterialRepository(filePath);

        var materials = await repository.GetAllAsync();

        var demo = Assert.Single(materials);
        Assert.Equal(DemoMaterialCatalog.Phase1.MaterialId, demo.MaterialId);
        Assert.Equal("Phase 2 seed", demo.ColorFinish);
        Assert.Equal("Seeded into the local material library on first run.", demo.Notes);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task Create_update_and_delete_round_trip_through_json_storage()
    {
        var filePath = Path.Combine(_workspacePath, "materials.json");
        var repository = new JsonMaterialRepository(filePath);

        var created = await repository.CreateAsync(
            new Material
            {
                MaterialId = "mdf-3-4",
                Name = "MDF 3/4 Premium",
                SheetLength = 97m,
                SheetWidth = 49m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m,
                Notes = "Shop stock",
                CostPerSheet = 61.25m
            });

        var fetched = await repository.GetByIdAsync(created.MaterialId);
        Assert.NotNull(fetched);
        Assert.Equal("MDF 3/4 Premium", fetched!.Name);

        var updated = await repository.UpdateAsync(created with
        {
            Name = "MDF 3/4 Prime",
            CostPerSheet = 64.50m
        });

        Assert.Equal("MDF 3/4 Prime", updated.Name);
        Assert.Equal(64.50m, updated.CostPerSheet);

        var reloadedRepository = new JsonMaterialRepository(filePath);
        var list = await reloadedRepository.GetAllAsync();
        Assert.Contains(
            list,
            material => material.MaterialId == "mdf-3-4" &&
                        material.Name == "MDF 3/4 Prime" &&
                        material.CostPerSheet == 64.50m);

        await reloadedRepository.DeleteAsync(created.MaterialId);
        var afterDelete = await reloadedRepository.GetAllAsync();
        Assert.DoesNotContain(afterDelete, material => material.MaterialId == created.MaterialId);

        var json = await File.ReadAllTextAsync(filePath);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
    }

    [Fact]
    public async Task Material_service_rejects_duplicate_material_names_case_insensitively()
    {
        var filePath = Path.Combine(_workspacePath, "materials.json");
        var repository = new JsonMaterialRepository(filePath);
        var service = new MaterialService(repository, idGenerator: () => Guid.NewGuid().ToString("N"));

        var first = await service.CreateAsync(
            new Material
            {
                Name = "Oak Veneer",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(first.Success);

        var second = await service.CreateAsync(
            new Material
            {
                Name = "oak veneer",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.False(second.Success);
        var error = Assert.Single(second.Errors);
        Assert.Equal("material-name-exists", error.Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }
}
