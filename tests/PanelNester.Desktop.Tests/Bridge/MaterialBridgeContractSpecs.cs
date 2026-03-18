using System.IO;
using System.Text.Json;
using PanelNester.Desktop.Bridge;
using PanelNester.Desktop.Tests.Specifications;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;
using PanelNester.Services.Projects;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class MaterialBridgeContractSpecs
{
    [Fact]
    public void Phase_two_material_bridge_message_names_follow_the_existing_request_response_pattern()
    {
        var responseTypes = Phase02BridgeExpectations.MaterialMessageTypes
            .Select(BridgeMessageTypes.ToResponseType)
            .ToArray();

        Assert.Equal(
            [
                "list-materials-response",
                "choose-material-library-location-response",
                "restore-default-material-library-location-response",
                "get-material-response",
                "create-material-response",
                "update-material-response",
                "delete-material-response"
            ],
            responseTypes);
    }

    [Fact]
    public void Handshake_and_dispatcher_expose_material_library_bridge_operations()
    {
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new StubFileDialogService(),
            new StubMaterialService(),
            new ProjectService(new StubMaterialService(), idGenerator: () => "project-contract"),
            new StubImportService(),
            new PartEditorService(DemoMaterialCatalog.All),
            new StubNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true),
            new StubMaterialLibraryLocationService());

        foreach (var messageType in Phase02BridgeExpectations.MaterialMessageTypes)
        {
            Assert.Contains(messageType, dispatcher.RegisteredTypes);
        }
    }

    [Fact]
    public void Material_library_location_payload_uses_the_web_contract_property_names()
    {
        var payload = JsonSerializer.SerializeToElement(
            new MaterialLibraryLocation
            {
                ActiveFilePath = @"F:\custom\materials.json",
                DefaultFilePath = @"F:\default\materials.json",
                UsesDefaultLocation = false
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.True(payload.TryGetProperty("currentPath", out var currentPath));
        Assert.True(payload.TryGetProperty("defaultPath", out var defaultPath));
        Assert.True(payload.TryGetProperty("usingDefaultLocation", out var usingDefaultLocation));
        Assert.False(payload.TryGetProperty("activeFilePath", out _));
        Assert.False(payload.TryGetProperty("defaultFilePath", out _));
        Assert.False(payload.TryGetProperty("usesDefaultLocation", out _));
        Assert.Equal(@"F:\custom\materials.json", currentPath.GetString());
        Assert.Equal(@"F:\default\materials.json", defaultPath.GetString());
        Assert.False(usingDefaultLocation.GetBoolean());
    }

    [Fact]
    public void Create_default_overloads_forward_existing_callers_into_the_same_registration_shape()
    {
        var fileDialogService = new StubFileDialogService();
        var materialService = new StubMaterialService();
        var projectService = new ProjectService(new StubMaterialService(), idGenerator: () => "project-overload");
        var importService = new StubImportService();
        var partEditorService = new PartEditorService(DemoMaterialCatalog.All);
        var nestingService = new StubNestingService();
        Func<WebUiContentLocation> contentLocationAccessor = () => new("F:\\mock-ui", "Mock UI build", true);

        var basicDispatcher = DesktopBridgeRegistration.CreateDefault(
            fileDialogService,
            materialService,
            projectService,
            importService,
            nestingService,
            contentLocationAccessor);
        var partEditorDispatcher = DesktopBridgeRegistration.CreateDefault(
            fileDialogService,
            materialService,
            projectService,
            importService,
            partEditorService,
            nestingService,
            contentLocationAccessor);
        var fullDispatcher = DesktopBridgeRegistration.CreateDefault(
            fileDialogService,
            materialService,
            projectService,
            importService,
            partEditorService,
            nestingService,
            null,
            null,
            null,
            contentLocationAccessor);

        Assert.Equal(basicDispatcher.RegisteredTypes, partEditorDispatcher.RegisteredTypes);
        Assert.Equal(partEditorDispatcher.RegisteredTypes, fullDispatcher.RegisteredTypes);
    }

    private sealed class StubFileDialogService : IFileDialogService
    {
        public Task<OpenFileDialogResponse> OpenAsync(OpenFileDialogRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(OpenFileDialogResponse.Cancelled());

        public Task<SaveFileDialogResponse> SaveAsync(SaveFileDialogRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(SaveFileDialogResponse.Cancelled());
    }

    private sealed class StubImportService : IImportService
    {
        public Task<ImportResponse> ImportAsync(ImportRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportResponse { Success = true });

        public Task<ImportResponse> ImportAsync(
            TextReader reader,
            ImportOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportResponse { Success = true });
    }

    private sealed class StubNestingService : INestingService
    {
        public Task<NestResponse> NestAsync(NestRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NestResponse { Success = true });
    }

    private sealed class StubMaterialService : IMaterialService
    {
        public Task<IReadOnlyList<Material>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Material>>(DemoMaterialCatalog.All);

        public Task<MaterialOperationResult> GetAsync(string materialId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterialOperationResult { Success = true, Material = DemoMaterialCatalog.Phase1 });

        public Task<MaterialOperationResult> CreateAsync(Material material, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterialOperationResult { Success = true, Material = material });

        public Task<MaterialOperationResult> UpdateAsync(Material material, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterialOperationResult { Success = true, Material = material });

        public Task<MaterialDeleteResult> DeleteAsync(string materialId, bool isInUse = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterialDeleteResult { Success = true });
    }

    private sealed class StubMaterialLibraryLocationService : IMaterialLibraryLocationService
    {
        public Task<MaterialLibraryLocation> GetLocationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateLocation());

        public Task<MaterialLibraryLocation> RepointAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateLocation());

        public Task<MaterialLibraryLocation> RestoreDefaultAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateLocation());

        private static MaterialLibraryLocation CreateLocation() =>
            new()
            {
                ActiveFilePath = @"F:\materials.json",
                DefaultFilePath = @"F:\materials.json",
                UsesDefaultLocation = true
            };
    }
}
