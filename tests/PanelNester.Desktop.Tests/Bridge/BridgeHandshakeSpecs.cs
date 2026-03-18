using System.IO;
using System.Text.Json;
using PanelNester.Desktop.Bridge;
using PanelNester.Desktop.Tests.Specifications;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;
using PanelNester.Services.Projects;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class BridgeHandshakeSpecs
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Bridge_message_envelope_preserves_type_request_id_and_payload_shape()
    {
        var message = new BridgeMessageEnvelope(
            BridgeMessageTypes.ToResponseType(BridgeMessageTypes.BridgeHandshake),
            "req-bridge-001",
            JsonSerializer.SerializeToElement(new { ok = true, source = "desktop" }, SerializerOptions));

        var json = JsonSerializer.Serialize(message, SerializerOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("bridge-handshake-response", root.GetProperty("type").GetString());
        Assert.Equal("req-bridge-001", root.GetProperty("requestId").GetString());
        Assert.True(root.GetProperty("payload").GetProperty("ok").GetBoolean());
        Assert.Equal("desktop", root.GetProperty("payload").GetProperty("source").GetString());
    }

    [Fact]
    public async Task Handshake_round_trip_returns_capabilities_with_matching_request_id()
    {
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new StubFileDialogService(),
            new StubMaterialService(),
            new ProjectService(new StubMaterialService(), idGenerator: () => "project-handshake"),
            new StubImportService(),
            new PartEditorService(DemoMaterialCatalog.All),
            new StubNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true),
            new StubMaterialLibraryLocationService());
        var request = new BridgeMessageEnvelope(
            BridgeMessageTypes.BridgeHandshake,
            "req-handshake-001",
            JsonSerializer.SerializeToElement(
                new BridgeHandshakeRequest(
                    "PanelNester.WebUI",
                    "0.1.0",
                    [
                        BridgeMessageTypes.BridgeHandshake,
                        BridgeMessageTypes.OpenFileDialog,
                        BridgeMessageTypes.ImportCsv,
                        BridgeMessageTypes.ListMaterials,
                        Phase02BridgeExpectations.MaterialMessageTypes[1],
                        Phase02BridgeExpectations.MaterialMessageTypes[2],
                        BridgeMessageTypes.RunNesting
                    ]),
                SerializerOptions));

        var response = await dispatcher.DispatchAsync(request);

        Assert.NotNull(response);
        Assert.Equal("req-handshake-001", response!.RequestId);
        Assert.Equal("bridge-handshake-response", response.Type);

        var payload = response.Payload.Deserialize<BridgeHandshakeResponse>(SerializerOptions);
        Assert.NotNull(payload);
        Assert.True(payload!.Success);
        Assert.Equal("PanelNester Desktop Host", payload.HostName);
        Assert.Equal("webview2", payload.BridgeMode);
        Assert.Contains(BridgeMessageTypes.ListMaterials, payload.Capabilities);
        Assert.Contains(Phase02BridgeExpectations.MaterialMessageTypes[1], payload.Capabilities);
        Assert.Contains(Phase02BridgeExpectations.MaterialMessageTypes[2], payload.Capabilities);
        Assert.Contains(BridgeMessageTypes.RunNesting, payload.Capabilities);
    }

    [Fact]
    public async Task Unknown_message_type_returns_actionable_error_without_hanging_the_ui()
    {
        var dispatcher = new BridgeMessageDispatcher();
        var response = await dispatcher.DispatchAsync(
            new BridgeMessageEnvelope(
                "totally-unknown",
                "req-unknown-001",
                JsonSerializer.SerializeToElement(new { ignored = true }, SerializerOptions)));

        Assert.NotNull(response);
        Assert.Equal("req-unknown-001", response!.RequestId);
        Assert.Equal("totally-unknown-response", response.Type);

        var payload = response.Payload.Deserialize<BridgeOperationResponse>(SerializerOptions);
        Assert.NotNull(payload);
        Assert.False(payload!.Success);
        Assert.NotNull(payload.Error);
        Assert.Equal("unsupported-message", payload.Error!.Code);
    }

    [Fact(Skip = "Blocked until WebView2 bootstrap failures are surfaced as testable host errors.")]
    public void Missing_webview2_runtime_reports_a_user_visible_startup_error()
    {
    }

    private sealed class StubFileDialogService : IFileDialogService
    {
        public Task<OpenFileDialogResponse> OpenAsync(
            OpenFileDialogRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new OpenFileDialogResponse(true, @"C:\mock.csv", null, "File selected."));

        public Task<SaveFileDialogResponse> SaveAsync(
            SaveFileDialogRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SaveFileDialogResponse(true, @"C:\mock.pnest", null, "File path selected."));
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

    private sealed class StubMaterialService : IMaterialService
    {
        public Task<IReadOnlyList<Material>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Material>>(DemoMaterialCatalog.All);

        public Task<MaterialOperationResult> GetAsync(string materialId, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new MaterialOperationResult
                {
                    Success = true,
                    Material = DemoMaterialCatalog.All.FirstOrDefault(material => material.MaterialId == materialId) ?? DemoMaterialCatalog.Phase1
                });

        public Task<MaterialOperationResult> CreateAsync(Material material, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterialOperationResult { Success = true, Material = material });

        public Task<MaterialOperationResult> UpdateAsync(Material material, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterialOperationResult { Success = true, Material = material });

        public Task<MaterialDeleteResult> DeleteAsync(string materialId, bool isInUse = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterialDeleteResult { Success = true });
    }

    private sealed class StubNestingService : INestingService
    {
        public Task<NestResponse> NestAsync(NestRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NestResponse { Success = true });
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
                ActiveFilePath = @"C:\mock\materials.json",
                DefaultFilePath = @"C:\mock\materials.json",
                UsesDefaultLocation = true
            };
    }
}
