using System.IO;
using System.Text.Json;
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
        public Task<OpenFileDialogResponse> OpenAsync(
            OpenFileDialogRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OpenFileDialogResponse.Cancelled());

        public Task<SaveFileDialogResponse> SaveAsync(
            SaveFileDialogRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SaveFileDialogResponse.Cancelled());
    }
}
