using System.IO;
using System.Text;
using System.Text.Json;
using PanelNester.Desktop.Bridge;
using PanelNester.Desktop.Tests.Specifications;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;
using PanelNester.Services.Materials;
using PanelNester.Services.Nesting;
using PanelNester.Services.Projects;
using PanelNester.Services.Reporting;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class Phase05BridgeSpecs : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.Phase05BridgeSpecs.{Guid.NewGuid():N}");

    [Fact]
    public void Phase_five_bridge_message_names_follow_the_existing_request_response_pattern()
    {
        var responseTypes = Phase05BridgeExpectations.MessageTypes
            .Select(BridgeMessageTypes.ToResponseType)
            .ToArray();

        Assert.Equal(
            [
                "run-batch-nesting-response",
                "update-report-settings-response",
                "export-pdf-report-response"
            ],
            responseTypes);
    }

    [Fact]
    public async Task Batch_nesting_report_settings_and_pdf_export_round_trip_through_the_desktop_bridge()
    {
        Directory.CreateDirectory(_workspacePath);

        var materialFilePath = Path.Combine(_workspacePath, "materials.json");
        var pdfPath = Path.Combine(_workspacePath, "batch-report.pdf");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: CreateMaterialIds());
        var projectService = new ProjectService(materialService, idGenerator: () => "project-phase5");
        var dialogs = new RecordingFileDialogService(savePaths: [pdfPath]);

        var birchResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Baltic Birch 18mm",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m,
                CostPerSheet = 120m
            });
        var mapleResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Maple Ply 18mm",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m,
                CostPerSheet = 132m
            });

        var birch = Assert.IsType<Material>(birchResult.Material);
        var maple = Assert.IsType<Material>(mapleResult.Material);

        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            dialogs,
            materialService,
            projectService,
            new FileImportDispatcher(new CsvImportService(repository), new XlsxImportService(repository)),
            new PartEditorService(repository),
            new ShelfNestingService(),
            new BatchNestingService(new ShelfNestingService()),
            new ReportDataService(),
            new QuestPdfReportExporter(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        foreach (var messageType in Phase05BridgeExpectations.MessageTypes)
        {
            Assert.Contains(messageType, dispatcher.RegisteredTypes);
        }

        var newProjectResponse = await DispatchAsync<NewProjectResponse>(
            dispatcher,
            BridgeMessageTypes.NewProject,
            new NewProjectRequest(
                new ProjectMetadata
                {
                    ProjectName = "Workshop Cabinets",
                    ProjectNumber = "PN-500",
                    CustomerName = "Northwind Fixtures",
                    Date = new DateTime(2026, 03, 14, 0, 0, 0, DateTimeKind.Utc)
                },
                new ProjectSettings
                {
                    KerfWidth = 0.0625m,
                    ReportSettings = new ReportSettings()
                }));

        var project = Assert.IsType<Project>(newProjectResponse.Project);

        var settingsResponse = await DispatchAsync<UpdateReportSettingsResponse>(
            dispatcher,
            BridgeMessageTypes.UpdateReportSettings,
            new UpdateReportSettingsRequest(
                project,
                new ReportSettings
                {
                    Notes = "Customer-facing report."
                }));

        Assert.True(settingsResponse.Success);
        project = Assert.IsType<Project>(settingsResponse.Project);
        var reportSettings = Assert.IsType<ReportSettings>(settingsResponse.ReportSettings);
        Assert.Equal("Northwind Fixtures", reportSettings.CompanyName);
        Assert.Equal("Workshop Cabinets Nesting Report", reportSettings.ReportTitle);
        Assert.Equal("Customer-facing report.", reportSettings.Notes);

        PartRow[] parts =
        [
            new PartRow
            {
                RowId = "row-1",
                ImportedId = "B-001",
                Length = 24m,
                Width = 12m,
                Quantity = 1,
                MaterialName = birch.Name,
                ValidationStatus = ValidationStatuses.Valid
            },
            new PartRow
            {
                RowId = "row-2",
                ImportedId = "M-001",
                Length = 18m,
                Width = 10m,
                Quantity = 1,
                MaterialName = maple.Name,
                ValidationStatus = ValidationStatuses.Valid
            }
        ];

        var batchResponse = await DispatchAsync<BatchNestResponse>(
            dispatcher,
            BridgeMessageTypes.RunBatchNesting,
            new BatchNestRequest
            {
                Parts = parts,
                Materials = [birch, maple],
                KerfWidth = project.Settings.KerfWidth,
                SelectedMaterialId = maple.MaterialId
            });

        Assert.True(batchResponse.Success);
        Assert.Equal(2, batchResponse.MaterialResults.Count);
        Assert.Equal("Maple Ply 18mm", batchResponse.MaterialResults[1].MaterialName);
        Assert.Equal(batchResponse.MaterialResults[1].Result, batchResponse.LegacyResult);

        var exportProject = project with
        {
            MaterialSnapshots = [birch, maple],
            State = new ProjectState
            {
                Parts = parts,
                SelectedMaterialId = maple.MaterialId,
                LastNestingResult = batchResponse.LegacyResult,
                LastBatchNestingResult = batchResponse
            }
        };

        var exportResponse = await DispatchAsync<ExportPdfReportResponse>(
            dispatcher,
            BridgeMessageTypes.ExportPdfReport,
            new ExportPdfReportRequest(exportProject, batchResponse));

        Assert.True(exportResponse.Success);
        Assert.Equal(pdfPath, exportResponse.FilePath);
        Assert.True(File.Exists(pdfPath));

        var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(pdfBytes.Length, 5)));

        var saveRequest = Assert.Single(dialogs.SaveRequests);
        Assert.Equal("Export PanelNester PDF report", saveRequest.Title);
        Assert.Equal(".pdf", saveRequest.DefaultExtension);
        Assert.Contains(saveRequest.Filters!, filter => filter.Extensions.Contains("pdf", StringComparer.Ordinal));
        Assert.Equal("Workshop Cabinets Nesting Report.pdf", saveRequest.FileName);
    }

    [Fact]
    public async Task Export_pdf_report_returns_cancelled_when_save_dialog_is_cancelled()
    {
        Directory.CreateDirectory(_workspacePath);

        var dialogs = new RecordingFileDialogService();
        var dispatcher = CreateDispatcher(dialogs, new QuestPdfReportExporter());
        var project = CreateMinimalProject();

        var response = await DispatchAsync<ExportPdfReportResponse>(
            dispatcher,
            BridgeMessageTypes.ExportPdfReport,
            new ExportPdfReportRequest(project));

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.Equal("cancelled", response.Error!.Code);
        Assert.Single(dialogs.SaveRequests);
    }

    [Fact]
    public async Task Export_pdf_report_succeeds_for_an_empty_project_without_batch_results()
    {
        Directory.CreateDirectory(_workspacePath);

        var pdfPath = Path.Combine(_workspacePath, "empty-report.pdf");
        var dialogs = new RecordingFileDialogService(savePaths: [pdfPath]);
        var dispatcher = CreateDispatcher(dialogs, new QuestPdfReportExporter());
        var project = CreateMinimalProject();

        var response = await DispatchAsync<ExportPdfReportResponse>(
            dispatcher,
            BridgeMessageTypes.ExportPdfReport,
            new ExportPdfReportRequest(project));

        Assert.True(response.Success);
        Assert.Equal(pdfPath, response.FilePath);
        Assert.Null(response.Error);
        Assert.True(File.Exists(pdfPath));

        var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(pdfBytes.Length, 5)));
    }

    [Fact]
    public async Task Export_pdf_report_returns_failure_when_exporter_throws()
    {
        Directory.CreateDirectory(_workspacePath);

        var pdfPath = Path.Combine(_workspacePath, "failed-report.pdf");
        var dialogs = new RecordingFileDialogService(savePaths: [pdfPath]);
        var dispatcher = CreateDispatcher(dialogs, new ThrowingPdfReportExporter());
        var project = CreateMinimalProject();

        var response = await DispatchAsync<ExportPdfReportResponse>(
            dispatcher,
            BridgeMessageTypes.ExportPdfReport,
            new ExportPdfReportRequest(project));

        Assert.False(response.Success);
        Assert.Equal(pdfPath, response.FilePath);
        Assert.NotNull(response.Error);
        Assert.Equal("report-export-failed", response.Error!.Code);
        Assert.Single(dialogs.SaveRequests);
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

    private static Func<string> CreateMaterialIds()
    {
        var ids = new Queue<string>(["mat-birch", "mat-maple"]);
        return () => ids.Dequeue();
    }

    private BridgeMessageDispatcher CreateDispatcher(
        IFileDialogService dialogs,
        IPdfReportExporter pdfReportExporter)
    {
        var materialFilePath = Path.Combine(_workspacePath, $"materials-{Guid.NewGuid():N}.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "mat-bridge");
        var projectService = new ProjectService(materialService, idGenerator: () => "project-phase5");

        return DesktopBridgeRegistration.CreateDefault(
            dialogs,
            materialService,
            projectService,
            new FileImportDispatcher(new CsvImportService(repository), new XlsxImportService(repository)),
            new PartEditorService(repository),
            new ShelfNestingService(),
            new BatchNestingService(new ShelfNestingService()),
            new ReportDataService(),
            pdfReportExporter,
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));
    }

    private static Project CreateMinimalProject() =>
        new()
        {
            ProjectId = "project-phase5",
            Metadata = new ProjectMetadata
            {
                ProjectName = "Export Test",
                ProjectNumber = "PN-500",
                CustomerName = "Northwind Fixtures",
                Date = new DateTime(2026, 03, 14, 0, 0, 0, DateTimeKind.Utc)
            },
            Settings = new ProjectSettings
            {
                ReportSettings = new ReportSettings()
            }
        };

    private sealed class RecordingFileDialogService(IEnumerable<string>? savePaths = null) : IFileDialogService
    {
        private readonly Queue<string> _savePaths = new(savePaths ?? []);

        public List<OpenFileDialogRequest> OpenRequests { get; } = [];

        public List<SaveFileDialogRequest> SaveRequests { get; } = [];

        public Task<OpenFileDialogResponse> OpenAsync(
            OpenFileDialogRequest request,
            CancellationToken cancellationToken = default)
        {
            OpenRequests.Add(request);
            return Task.FromResult(OpenFileDialogResponse.Cancelled());
        }

        public Task<SaveFileDialogResponse> SaveAsync(
            SaveFileDialogRequest request,
            CancellationToken cancellationToken = default)
        {
            SaveRequests.Add(request);
            return Task.FromResult(
                _savePaths.Count == 0
                    ? SaveFileDialogResponse.Cancelled()
                    : new SaveFileDialogResponse(true, _savePaths.Dequeue(), null, "File path selected."));
        }
    }

    private sealed class ThrowingPdfReportExporter : IPdfReportExporter
    {
        public Task ExportAsync(ReportData report, string filePath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Report export failed.");
    }
}
