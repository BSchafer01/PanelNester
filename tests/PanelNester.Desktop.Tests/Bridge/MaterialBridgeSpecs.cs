using System.IO;
using System.Text.Json;
using PanelNester.Desktop;
using PanelNester.Desktop.Bridge;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;
using PanelNester.Services.Materials;
using PanelNester.Services.Nesting;
using PanelNester.Services.Projects;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class MaterialBridgeSpecs : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.MaterialBridgeSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Material_crud_messages_round_trip_through_the_desktop_bridge()
    {
        var materialFilePath = Path.Combine(_workspacePath, "materials.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "birch-ply-18");
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new StubFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-material-bridge"),
            new CsvImportService(repository),
            new PartEditorService(repository),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var initialList = await DispatchAsync<ListMaterialsResponse>(
            dispatcher,
            BridgeMessageTypes.ListMaterials,
            new ListMaterialsRequest());

        Assert.True(initialList.Success);
        Assert.Single(initialList.Materials);
        Assert.Equal(DemoMaterialCatalog.Phase1.MaterialId, initialList.Materials[0].MaterialId);

        var createdMaterial = new Material
        {
            MaterialId = "birch-ply-18",
            Name = "Birch Ply 18mm",
            SheetLength = 120m,
            SheetWidth = 60m,
            AllowRotation = true,
            DefaultSpacing = 0.125m,
            DefaultEdgeMargin = 0.5m,
            ColorFinish = "Natural",
            Notes = "Preferred cabinet stock",
            CostPerSheet = 142.75m
        };

        var createResponse = await DispatchAsync<CreateMaterialResponse>(
            dispatcher,
            BridgeMessageTypes.CreateMaterial,
            new CreateMaterialRequest(createdMaterial));

        Assert.True(createResponse.Success);
        Assert.NotNull(createResponse.Material);
        Assert.Equal("birch-ply-18", createResponse.Material!.MaterialId);
        Assert.Equal("Birch Ply 18mm", createResponse.Material!.Name);

        var getResponse = await DispatchAsync<GetMaterialResponse>(
            dispatcher,
            BridgeMessageTypes.GetMaterial,
            new GetMaterialRequest(createResponse.Material.MaterialId));

        Assert.True(getResponse.Success);
        Assert.NotNull(getResponse.Material);
        Assert.Equal(createResponse.Material.MaterialId, getResponse.Material!.MaterialId);

        var updatedMaterial = createResponse.Material! with
        {
            Name = "Birch Ply 18mm Updated",
            DefaultSpacing = 0.1875m
        };

        var updateResponse = await DispatchAsync<UpdateMaterialResponse>(
            dispatcher,
            BridgeMessageTypes.UpdateMaterial,
            new UpdateMaterialRequest(updatedMaterial));

        Assert.True(updateResponse.Success);
        Assert.NotNull(updateResponse.Material);
        Assert.Equal("Birch Ply 18mm Updated", updateResponse.Material!.Name);
        Assert.Equal(0.1875m, updateResponse.Material.DefaultSpacing);

        var deleteResponse = await DispatchAsync<DeleteMaterialResponse>(
            dispatcher,
            BridgeMessageTypes.DeleteMaterial,
            new DeleteMaterialRequest(createResponse.Material.MaterialId));

        Assert.True(deleteResponse.Success);
        Assert.Equal(createResponse.Material.MaterialId, deleteResponse.MaterialId);

        var finalList = await DispatchAsync<ListMaterialsResponse>(
            dispatcher,
            BridgeMessageTypes.ListMaterials,
            new ListMaterialsRequest());

        Assert.True(finalList.Success);
        Assert.Single(finalList.Materials);
        Assert.DoesNotContain(finalList.Materials, material => material.MaterialId == createResponse.Material.MaterialId);
        Assert.True(File.Exists(materialFilePath));
    }

    [Fact]
    public async Task Material_library_location_can_be_repointed_persisted_and_restored_to_a_recreated_default_file()
    {
        var defaultMaterialFilePath = Path.Combine(_workspacePath, "default", "materials.json");
        var locationStoreFilePath = Path.Combine(_workspacePath, "settings", "material-library-location.json");
        var customMaterialFilePath = Path.Combine(_workspacePath, "custom", "materials.alt.json");
        var fileDialogService = new StubFileDialogService(customMaterialFilePath);
        var repository = new JsonMaterialRepository(
            new JsonMaterialRepositoryOptions
            {
                DefaultFilePath = defaultMaterialFilePath,
                LocationStoreFilePath = locationStoreFilePath
            });
        var materialService = new MaterialService(repository, idGenerator: () => "walnut-ply");
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            fileDialogService,
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-material-location"),
            new CsvImportService(repository),
            new PartEditorService(repository),
            new ShelfNestingService(),
            null,
            null,
            null,
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true),
            materialLibraryLocationService: repository);

        var initialList = await DispatchAsync<ListMaterialsResponse>(
            dispatcher,
            BridgeMessageTypes.ListMaterials,
            new ListMaterialsRequest());

        Assert.True(initialList.Success);
        Assert.NotNull(initialList.LibraryLocation);
        Assert.Equal(Path.GetFullPath(defaultMaterialFilePath), initialList.LibraryLocation!.ActiveFilePath);
        Assert.Equal(Path.GetFullPath(defaultMaterialFilePath), initialList.LibraryLocation.DefaultFilePath);
        Assert.True(initialList.LibraryLocation.UsesDefaultLocation);

        var changeResponse = await DispatchAsync<ChooseMaterialLibraryLocationResponse>(
            dispatcher,
            BridgeMessageTypes.ChooseMaterialLibraryLocation,
            new ChooseMaterialLibraryLocationRequest());

        Assert.True(changeResponse.Success);
        Assert.NotNull(changeResponse.LibraryLocation);
        Assert.Equal(Path.GetFullPath(customMaterialFilePath), changeResponse.LibraryLocation!.ActiveFilePath);
        Assert.Equal(Path.GetFullPath(defaultMaterialFilePath), changeResponse.LibraryLocation.DefaultFilePath);
        Assert.False(changeResponse.LibraryLocation.UsesDefaultLocation);
        Assert.True(File.Exists(customMaterialFilePath));
        Assert.Single(changeResponse.Materials);
        Assert.NotNull(fileDialogService.LastSaveRequest);
        Assert.Equal("Choose material library location", fileDialogService.LastSaveRequest!.Title);
        Assert.Equal("materials.json", fileDialogService.LastSaveRequest.FileName);
        Assert.Equal(".json", fileDialogService.LastSaveRequest.DefaultExtension);
        Assert.False(fileDialogService.LastSaveRequest.OverwritePrompt);
        Assert.Contains(fileDialogService.LastSaveRequest.Filters!, filter => filter.Name == "Material library files");

        var restartedRepository = new JsonMaterialRepository(
            new JsonMaterialRepositoryOptions
            {
                DefaultFilePath = defaultMaterialFilePath,
                LocationStoreFilePath = locationStoreFilePath
            });
        var restartedLocation = await restartedRepository.GetLocationAsync();
        Assert.Equal(Path.GetFullPath(customMaterialFilePath), restartedLocation.ActiveFilePath);

        var createResponse = await DispatchAsync<CreateMaterialResponse>(
            dispatcher,
            BridgeMessageTypes.CreateMaterial,
            new CreateMaterialRequest(
                new Material
                {
                    MaterialId = "walnut-ply",
                    Name = "Walnut Ply",
                    SheetLength = 96m,
                    SheetWidth = 48m,
                    AllowRotation = true,
                    DefaultSpacing = 0.125m,
                    DefaultEdgeMargin = 0.5m
                }));

        Assert.True(createResponse.Success);
        var customMaterials = await new JsonMaterialRepository(customMaterialFilePath).GetAllAsync();
        Assert.Contains(customMaterials, material => material.MaterialId == "walnut-ply");

        if (File.Exists(defaultMaterialFilePath))
        {
            File.Delete(defaultMaterialFilePath);
        }

        var restoreResponse = await DispatchAsync<RestoreDefaultMaterialLibraryLocationResponse>(
            dispatcher,
            BridgeMessageTypes.RestoreDefaultMaterialLibraryLocation,
            new RestoreDefaultMaterialLibraryLocationRequest());

        Assert.True(restoreResponse.Success);
        Assert.NotNull(restoreResponse.LibraryLocation);
        Assert.Equal(Path.GetFullPath(defaultMaterialFilePath), restoreResponse.LibraryLocation!.ActiveFilePath);
        Assert.True(restoreResponse.LibraryLocation.UsesDefaultLocation);
        Assert.True(File.Exists(defaultMaterialFilePath));
        var restoredDemo = Assert.Single(restoreResponse.Materials);
        Assert.Equal(DemoMaterialCatalog.Phase1.MaterialId, restoredDemo.MaterialId);

        var restartedDefaultRepository = new JsonMaterialRepository(
            new JsonMaterialRepositoryOptions
            {
                DefaultFilePath = defaultMaterialFilePath,
                LocationStoreFilePath = locationStoreFilePath
            });
        var restartedDefaultLocation = await restartedDefaultRepository.GetLocationAsync();
        Assert.Equal(Path.GetFullPath(defaultMaterialFilePath), restartedDefaultLocation.ActiveFilePath);
    }

    [Fact]
    public async Task Choosing_material_library_location_can_be_cancelled_without_changing_the_active_repository()
    {
        var defaultMaterialFilePath = Path.Combine(_workspacePath, "cancel-default", "materials.json");
        var locationStoreFilePath = Path.Combine(_workspacePath, "cancel-settings", "material-library-location.json");
        var repository = new JsonMaterialRepository(
            new JsonMaterialRepositoryOptions
            {
                DefaultFilePath = defaultMaterialFilePath,
                LocationStoreFilePath = locationStoreFilePath
            });
        var materialService = new MaterialService(repository);
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new StubFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-material-location-cancel"),
            new CsvImportService(repository),
            new PartEditorService(repository),
            new ShelfNestingService(),
            null,
            null,
            null,
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true),
            materialLibraryLocationService: repository);

        var response = await DispatchAsync<ChooseMaterialLibraryLocationResponse>(
            dispatcher,
            BridgeMessageTypes.ChooseMaterialLibraryLocation,
            new ChooseMaterialLibraryLocationRequest());

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Equal("cancelled", response.Error!.Code);

        var location = await repository.GetLocationAsync();
        Assert.Equal(Path.GetFullPath(defaultMaterialFilePath), location.ActiveFilePath);
        Assert.True(location.UsesDefaultLocation);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }

    private static async Task<TResponse> DispatchAsync<TResponse>(
        BridgeMessageDispatcher dispatcher,
        string type,
        object payload)
    {
        var response = await dispatcher.DispatchAsync(
            new BridgeMessageEnvelope(
                type,
                Guid.NewGuid().ToString("N"),
                JsonSerializer.SerializeToElement(payload, SerializerOptions)));

        Assert.NotNull(response);
        var typed = response!.Payload.Deserialize<TResponse>(SerializerOptions);
        Assert.NotNull(typed);
        return typed!;
    }

    private sealed class StubFileDialogService : IFileDialogService
    {
        private readonly string? _saveFilePath;

        public StubFileDialogService(string? saveFilePath = null)
        {
            _saveFilePath = saveFilePath;
        }

        public SaveFileDialogRequest? LastSaveRequest { get; private set; }

        public Task<OpenFileDialogResponse> OpenAsync(
            OpenFileDialogRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OpenFileDialogResponse.Cancelled());

        public Task<SaveFileDialogResponse> SaveAsync(
            SaveFileDialogRequest request,
            CancellationToken cancellationToken = default)
        {
            LastSaveRequest = request;
            return Task.FromResult(
                string.IsNullOrWhiteSpace(_saveFilePath)
                    ? SaveFileDialogResponse.Cancelled()
                    : new SaveFileDialogResponse(true, _saveFilePath, null, "File path selected."));
        }
    }
}
