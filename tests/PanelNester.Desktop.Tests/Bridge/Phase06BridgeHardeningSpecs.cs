using System.IO;
using System.Text;
using System.Text.Json;
using PanelNester.Desktop.Bridge;
using PanelNester.Domain.Contracts;
using PanelNester.Services.Import;
using PanelNester.Services.Materials;
using PanelNester.Services.Nesting;
using PanelNester.Services.Projects;
using PanelNester.Services.Reporting;
using PanelNester.Domain.Models;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class Phase06BridgeHardeningSpecs : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.Phase06BridgeHardeningSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Unknown_messages_include_a_friendly_user_message()
    {
        var dispatcher = new BridgeMessageDispatcher();

        var response = await dispatcher.DispatchAsync(
            new BridgeMessageEnvelope(
                "phase6-unknown",
                "req-phase6-unknown",
                JsonSerializer.SerializeToElement(new { ignored = true }, SerializerOptions)));

        var payload = DeserializePayload<BridgeOperationResponse>(response);

        Assert.False(payload.Success);
        Assert.NotNull(payload.Error);
        Assert.Equal("unsupported-message", payload.Error!.Code);
        Assert.Equal("This action is not available in the current desktop host.", payload.Error.UserMessage);
        Assert.Equal(payload.Error.UserMessage, payload.Message);
    }

    [Fact]
    public async Task Unexpected_handler_exceptions_return_a_generic_user_message()
    {
        var dispatcher = new BridgeMessageDispatcher();
        dispatcher.Register<object>(
            "phase6-explode",
            (_, _) => throw new InvalidOperationException("Bridge handler exploded in a test-only path."));

        var response = await dispatcher.DispatchAsync(
            new BridgeMessageEnvelope(
                "phase6-explode",
                "req-phase6-explode",
                JsonSerializer.SerializeToElement(new { ignored = true }, SerializerOptions)));

        var payload = DeserializePayload<BridgeOperationResponse>(response);

        Assert.False(payload.Success);
        Assert.NotNull(payload.Error);
        Assert.Equal("host-error", payload.Error!.Code);
        Assert.Equal("Bridge handler exploded in a test-only path.", payload.Error.Message);
        Assert.Equal("The desktop host ran into an unexpected problem. Please try again.", payload.Error.UserMessage);
        Assert.Equal(payload.Error.UserMessage, payload.Message);
    }

    [Fact]
    public async Task Material_bridge_validation_errors_include_nontechnical_user_message()
    {
        var dispatcher = CreateProjectDispatcher(new SequencedFileDialogService());

        var response = await DispatchAsync<GetMaterialResponse>(
            dispatcher,
            BridgeMessageTypes.GetMaterial,
            new GetMaterialRequest(string.Empty));

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Equal("material-id-required", response.Error!.Code);
        Assert.Equal("A materialId is required.", response.Error.Message);
        Assert.Equal("Choose a material and try again.", response.Error.UserMessage);
        Assert.Equal(response.Error.UserMessage, response.Message);
    }

    [Fact]
    public async Task Export_pdf_dialog_cancel_can_be_retried_without_noisy_error_messages()
    {
        Directory.CreateDirectory(_workspacePath);

        var pdfPath = Path.Combine(_workspacePath, "phase6-report.pdf");
        var dialogs = new SequencedFileDialogService(
            saveResponses:
            [
                SaveFileDialogResponse.Cancelled(),
                SaveFileDialogResponse.Cancelled(),
                new SaveFileDialogResponse(true, pdfPath, null, "File path selected.")
            ]);
        var dispatcher = CreateExportDispatcher(dialogs, new RecordingPdfExporter());
        var project = CreateMinimalProject();

        var first = await DispatchAsync<ExportPdfReportResponse>(
            dispatcher,
            BridgeMessageTypes.ExportPdfReport,
            new ExportPdfReportRequest(project));
        var second = await DispatchAsync<ExportPdfReportResponse>(
            dispatcher,
            BridgeMessageTypes.ExportPdfReport,
            new ExportPdfReportRequest(project));
        var third = await DispatchAsync<ExportPdfReportResponse>(
            dispatcher,
            BridgeMessageTypes.ExportPdfReport,
            new ExportPdfReportRequest(project));

        Assert.False(first.Success);
        Assert.Equal("cancelled", first.Error!.Code);
        Assert.Null(first.Error.UserMessage);
        Assert.Equal("PDF export was cancelled.", first.Message);

        Assert.False(second.Success);
        Assert.Equal("cancelled", second.Error!.Code);
        Assert.Null(second.Error.UserMessage);
        Assert.Equal("PDF export was cancelled.", second.Message);

        Assert.True(third.Success);
        Assert.Equal(pdfPath, third.FilePath);
        Assert.True(File.Exists(pdfPath));
        Assert.Equal(3, dialogs.SaveRequests.Count);
    }

    [Fact]
    public async Task Project_save_as_can_retry_after_multiple_cancels()
    {
        Directory.CreateDirectory(_workspacePath);

        var projectPath = Path.Combine(_workspacePath, "phase6-project.pnest");
        var dialogs = new SequencedFileDialogService(
            saveResponses:
            [
                SaveFileDialogResponse.Cancelled(),
                SaveFileDialogResponse.Cancelled(),
                new SaveFileDialogResponse(true, projectPath, null, "File path selected.")
            ]);
        var dispatcher = CreateProjectDispatcher(dialogs);
        var project = CreateMinimalProject();

        var first = await DispatchAsync<SaveProjectAsResponse>(
            dispatcher,
            BridgeMessageTypes.SaveProjectAs,
            new SaveProjectAsRequest(project, SuggestedFileName: "phase6-project"));
        var second = await DispatchAsync<SaveProjectAsResponse>(
            dispatcher,
            BridgeMessageTypes.SaveProjectAs,
            new SaveProjectAsRequest(project, SuggestedFileName: "phase6-project"));
        var third = await DispatchAsync<SaveProjectAsResponse>(
            dispatcher,
            BridgeMessageTypes.SaveProjectAs,
            new SaveProjectAsRequest(project, SuggestedFileName: "phase6-project"));

        Assert.False(first.Success);
        Assert.Equal("cancelled", first.Error!.Code);
        Assert.Null(first.Error.UserMessage);
        Assert.Equal("Project save was cancelled.", first.Message);

        Assert.False(second.Success);
        Assert.Equal("cancelled", second.Error!.Code);
        Assert.Null(second.Error.UserMessage);
        Assert.Equal("Project save was cancelled.", second.Message);

        Assert.True(third.Success);
        Assert.Equal(projectPath, third.FilePath);
        Assert.True(File.Exists(projectPath));
        Assert.Equal(3, dialogs.SaveRequests.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }

    private BridgeMessageDispatcher CreateProjectDispatcher(IFileDialogService dialogs)
    {
        var materialFilePath = Path.Combine(_workspacePath, $"materials-{Guid.NewGuid():N}.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "mat-phase6");
        var projectService = new ProjectService(materialService, idGenerator: () => "project-phase6");

        return DesktopBridgeRegistration.CreateDefault(
            dialogs,
            materialService,
            projectService,
            new FileImportDispatcher(new CsvImportService(repository), new XlsxImportService(repository)),
            new PartEditorService(repository),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));
    }

    private BridgeMessageDispatcher CreateExportDispatcher(IFileDialogService dialogs, IPdfReportExporter exporter)
    {
        var materialFilePath = Path.Combine(_workspacePath, $"materials-{Guid.NewGuid():N}.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "mat-phase6");
        var projectService = new ProjectService(materialService, idGenerator: () => "project-phase6");

        return DesktopBridgeRegistration.CreateDefault(
            dialogs,
            materialService,
            projectService,
            new FileImportDispatcher(new CsvImportService(repository), new XlsxImportService(repository)),
            new PartEditorService(repository),
            new ShelfNestingService(),
            new BatchNestingService(new ShelfNestingService()),
            new ReportDataService(),
            exporter,
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));
    }

    private static Project CreateMinimalProject() =>
        new()
        {
            ProjectId = "project-phase6",
            Metadata = new ProjectMetadata
            {
                ProjectName = "Phase 6 Retry Test",
                ProjectNumber = "PN-600",
                CustomerName = "Northwind Fixtures",
                Date = new DateTime(2026, 03, 15, 0, 0, 0, DateTimeKind.Utc)
            },
            Settings = new ProjectSettings
            {
                KerfWidth = 0.0625m,
                ReportSettings = new ReportSettings
                {
                    ReportTitle = "Phase 6 Retry Test"
                }
            }
        };

    private static Task<TResponse> DispatchAsync<TResponse>(
        BridgeMessageDispatcher dispatcher,
        string type,
        object payload) =>
        DispatchAsyncCore<TResponse>(
            dispatcher,
            new BridgeMessageEnvelope(
                type,
                Guid.NewGuid().ToString("N"),
                JsonSerializer.SerializeToElement(payload, SerializerOptions)));

    private static async Task<TResponse> DispatchAsyncCore<TResponse>(
        BridgeMessageDispatcher dispatcher,
        BridgeMessageEnvelope envelope)
    {
        var response = await dispatcher.DispatchAsync(envelope);
        return DeserializePayload<TResponse>(response);
    }

    private static TPayload DeserializePayload<TPayload>(BridgeMessageEnvelope? response)
    {
        Assert.NotNull(response);
        var typed = response!.Payload.Deserialize<TPayload>(SerializerOptions);
        Assert.NotNull(typed);
        return typed!;
    }

    private sealed class SequencedFileDialogService(
        IEnumerable<OpenFileDialogResponse>? openResponses = null,
        IEnumerable<SaveFileDialogResponse>? saveResponses = null) : IFileDialogService
    {
        private readonly Queue<OpenFileDialogResponse> _openResponses = new(openResponses ?? []);
        private readonly Queue<SaveFileDialogResponse> _saveResponses = new(saveResponses ?? []);

        public List<OpenFileDialogRequest> OpenRequests { get; } = [];

        public List<SaveFileDialogRequest> SaveRequests { get; } = [];

        public Task<OpenFileDialogResponse> OpenAsync(
            OpenFileDialogRequest request,
            CancellationToken cancellationToken = default)
        {
            OpenRequests.Add(request);
            return Task.FromResult(
                _openResponses.Count == 0
                    ? OpenFileDialogResponse.Cancelled()
                    : _openResponses.Dequeue());
        }

        public Task<SaveFileDialogResponse> SaveAsync(
            SaveFileDialogRequest request,
            CancellationToken cancellationToken = default)
        {
            SaveRequests.Add(request);
            return Task.FromResult(
                _saveResponses.Count == 0
                    ? SaveFileDialogResponse.Cancelled()
                    : _saveResponses.Dequeue());
        }
    }

    private sealed class RecordingPdfExporter : IPdfReportExporter
    {
        public Task ExportAsync(ReportData report, string filePath, CancellationToken cancellationToken = default) =>
            File.WriteAllBytesAsync(filePath, Encoding.ASCII.GetBytes("%PDF-phase6"), cancellationToken);
    }
}
