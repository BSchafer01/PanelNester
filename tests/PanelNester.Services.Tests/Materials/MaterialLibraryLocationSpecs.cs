using PanelNester.Domain.Models;
using PanelNester.Services.Materials;
using PanelNester.Services.Tests.Specifications;

namespace PanelNester.Services.Tests.Materials;

public sealed class MaterialLibraryLocationSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.MaterialLibraryLocationSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Repoint_normalizes_the_selected_json_path_persists_it_and_reloads_the_repository()
    {
        var options = MaterialLibraryLocationSpec.CreateOptions(_workspacePath);
        var repository = new JsonMaterialRepository(options);
        var requestedPath = $"  {Path.Combine(_workspacePath, "custom-library", "..", "custom-library", "materials.json")}  ";

        var location = await repository.RepointAsync(requestedPath);
        var created = await repository.CreateAsync(MaterialLibraryLocationSpec.CreateMaterial("walnut-ply", "Walnut Ply"));

        Assert.Equal(Path.GetFullPath(requestedPath.Trim()), location.ActiveFilePath);
        Assert.Equal(Path.GetFullPath(options.DefaultFilePath!), location.DefaultFilePath);
        Assert.False(location.UsesDefaultLocation);
        Assert.True(File.Exists(location.ActiveFilePath));

        var persistedPath = await MaterialLibraryLocationSpec.TryLoadStoredActiveLibraryPathAsync(options.LocationStoreFilePath!);
        Assert.Equal(location.ActiveFilePath, persistedPath);

        var restartedRepository = new JsonMaterialRepository(options);
        var restartedLocation = await restartedRepository.GetLocationAsync();
        var restartedMaterials = await restartedRepository.GetAllAsync();

        Assert.Equal(location.ActiveFilePath, restartedLocation.ActiveFilePath);
        Assert.False(restartedLocation.UsesDefaultLocation);
        Assert.Contains(restartedMaterials, material => material.MaterialId == created.MaterialId);
    }

    [Fact]
    public async Task Restore_default_clears_the_persisted_custom_location_and_recreates_the_default_file_when_needed()
    {
        var options = MaterialLibraryLocationSpec.CreateOptions(_workspacePath);
        var repository = new JsonMaterialRepository(options);
        var customLibraryPath = Path.Combine(_workspacePath, "custom-library", "materials.json");

        await repository.RepointAsync(customLibraryPath);

        if (File.Exists(options.DefaultFilePath))
        {
            File.Delete(options.DefaultFilePath);
        }

        var restoredLocation = await repository.RestoreDefaultAsync();
        var restartedRepository = new JsonMaterialRepository(options);
        var restartedLocation = await restartedRepository.GetLocationAsync();
        var restartedMaterials = await restartedRepository.GetAllAsync();

        Assert.Equal(Path.GetFullPath(options.DefaultFilePath!), restoredLocation.ActiveFilePath);
        Assert.True(restoredLocation.UsesDefaultLocation);
        Assert.True(File.Exists(options.DefaultFilePath!));
        Assert.False(File.Exists(options.LocationStoreFilePath!));
        Assert.Equal(restoredLocation.ActiveFilePath, restartedLocation.ActiveFilePath);
        Assert.True(restartedLocation.UsesDefaultLocation);

        var seededMaterial = Assert.Single(restartedMaterials);
        Assert.Equal(DemoMaterialCatalog.Phase1.MaterialId, seededMaterial.MaterialId);
    }

    [Fact]
    public async Task Persisted_custom_location_that_goes_missing_falls_back_to_default_and_clears_the_store()
    {
        var options = MaterialLibraryLocationSpec.CreateOptions(_workspacePath);
        var repository = new JsonMaterialRepository(options);
        var customLibraryPath = Path.Combine(_workspacePath, "custom-library", "materials.json");

        await repository.RepointAsync(customLibraryPath);
        await repository.CreateAsync(MaterialLibraryLocationSpec.CreateMaterial("maple-ply", "Maple Ply"));
        File.Delete(customLibraryPath);

        var restartedRepository = new JsonMaterialRepository(options);
        var recoveredLocation = await restartedRepository.GetLocationAsync();
        var recoveredMaterials = await restartedRepository.GetAllAsync();

        Assert.Equal(Path.GetFullPath(options.DefaultFilePath!), recoveredLocation.ActiveFilePath);
        Assert.True(recoveredLocation.UsesDefaultLocation);
        Assert.True(File.Exists(options.DefaultFilePath!));
        Assert.False(File.Exists(options.LocationStoreFilePath!));

        var seededMaterial = Assert.Single(recoveredMaterials);
        Assert.Equal(DemoMaterialCatalog.Phase1.MaterialId, seededMaterial.MaterialId);
    }

    [Fact]
    public async Task Repoint_rejects_invalid_material_payloads_without_changing_the_active_location()
    {
        var options = MaterialLibraryLocationSpec.CreateOptions(_workspacePath);
        var repository = new JsonMaterialRepository(options);
        var invalidLibraryPath = Path.Combine(_workspacePath, "invalid-library", "materials.json");

        _ = await repository.GetAllAsync();
        await MaterialLibraryLocationSpec.WriteRawLibraryFileAsync(
            invalidLibraryPath,
            """
            [
              {
                "materialId": "broken-material",
                "name": " ",
                "sheetLength": 96,
                "sheetWidth": 48,
                "allowRotation": true,
                "defaultSpacing": 0.125,
                "defaultEdgeMargin": 0.5
              }
            ]
            """);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => repository.RepointAsync(invalidLibraryPath));
        var location = await repository.GetLocationAsync();

        Assert.Contains(Path.GetFullPath(invalidLibraryPath), exception.Message);
        Assert.Equal(Path.GetFullPath(options.DefaultFilePath!), location.ActiveFilePath);
        Assert.True(location.UsesDefaultLocation);
        Assert.False(File.Exists(options.LocationStoreFilePath!));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }
}
