using System.IO;
using PanelNester.Desktop.Bridge;
using PanelNester.Desktop.Tests.Specifications;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;
using PanelNester.Services.Projects;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class ProjectBridgeContractSpecs
{
    [Fact]
    public void Phase_three_project_bridge_message_names_follow_the_existing_request_response_pattern()
    {
        var responseTypes = Phase03ProjectBridgeExpectations.ProjectMessageTypes
            .Select(BridgeMessageTypes.ToResponseType)
            .ToArray();

        Assert.Equal(
            [
                "new-project-response",
                "open-project-response",
                "save-project-response",
                "save-project-as-response",
                "get-project-metadata-response",
                "update-project-metadata-response"
            ],
            responseTypes);
    }

    [Fact]
    public void Phase_three_project_bridge_error_codes_match_the_project_persistence_contract()
    {
        Assert.Equal(
            ["project-not-found", "project-corrupt", "project-unsupported-version", "project-save-failed"],
            Phase03ProjectBridgeExpectations.ProjectErrorCodes);
    }

    [Fact]
    public void Handshake_and_dispatcher_expose_project_persistence_operations()
    {
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new StubFileDialogService(),
            new StubMaterialService(),
            new ProjectService(new StubMaterialService(), idGenerator: () => "project-contract"),
            new StubImportService(),
            new PartEditorService(DemoMaterialCatalog.All),
            new StubNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        foreach (var messageType in Phase03ProjectBridgeExpectations.ProjectMessageTypes)
        {
            Assert.Contains(messageType, dispatcher.RegisteredTypes);
        }
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
            Task.FromResult(new MaterialOperationResult { Success = true, Material = DemoMaterialCatalog.Phase1 });

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
}
