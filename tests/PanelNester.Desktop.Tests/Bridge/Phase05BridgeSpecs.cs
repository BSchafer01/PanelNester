using System.IO;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
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
    private static readonly JsonSerializerOptions SerializerOptions = BridgeJson.SerializerOptions;
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.Phase05BridgeSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Stock_Length_Excel_export_uses_the_visible_scope_and_normal_save_dialog()
    {
        Directory.CreateDirectory(_workspacePath);
        var xlsxPath = Path.Combine(_workspacePath, "visible-cut-plans.xlsx");
        var dialogs = new RecordingFileDialogService(savePaths: [xlsxPath]);
        var dispatcher = CreateDispatcher(
            dialogs,
            new QuestPdfReportExporter(),
            excelReportExporter: new ClosedXmlExcelReportExporter());
        var project = new Project
        {
            ProjectKind = ProjectKind.StockLength,
            State = new ProjectState
            {
                OptimizationGroups =
                [
                    StockLengthGroup("frames", "Frames", 0, "P-100", "Clear"),
                    StockLengthGroup("doors", "Doors", 1, "P-200", "Bronze")
                ]
            }
        };

        var response = await DispatchAsync<ExportExcelReportResponse>(
            dispatcher,
            BridgeMessageTypes.ExportExcelReport,
            new ExportExcelReportRequest(
                project,
                StockLengthScope: new StockLengthReportScope
                {
                    OptimizationGroupId = "frames"
                }));

        Assert.True(response.Success);
        Assert.Equal(xlsxPath, response.FilePath);
        Assert.Contains(dialogs.SaveRequests, request =>
            request.DefaultExtension == ".xlsx" && request.Title?.Contains("Excel", StringComparison.Ordinal) == true);
        using var workbook = new XLWorkbook(xlsxPath);
        var summary = workbook.Worksheet("Summary");
        Assert.Equal("Frames", summary.Cell(2, 1).GetString());
        Assert.DoesNotContain(summary.RowsUsed(), row => row.Cell(1).GetString() == "Doors");
    }

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
                "export-pdf-report-response",
                "export-excel-report-response"
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
            new ClosedXmlExcelReportExporter(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true),
            exportedPdfOpener: static _ => { });

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
        Assert.Equal("Export OptiFab PDF report", saveRequest.Title);
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
    public async Task Export_excel_report_writes_a_workbook_for_grouped_batch_results()
    {
        Directory.CreateDirectory(_workspacePath);

        var xlsxPath = Path.Combine(_workspacePath, "grouped-summary.xlsx");
        var dialogs = new RecordingFileDialogService(savePaths: [xlsxPath]);
        var dispatcher = CreateDispatcher(
            dialogs,
            new QuestPdfReportExporter(),
            excelReportExporter: new ClosedXmlExcelReportExporter());

        var material = new Material
        {
            MaterialId = "mat-grouped",
            Name = "Grouped Birch",
            SheetLength = 96m,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = 0m,
            DefaultEdgeMargin = 0m
        };

        var batchResponse = new BatchNestResponse
        {
            Success = true,
            MaterialResults =
            [
                new MaterialNestResult
                {
                    MaterialName = material.Name,
                    MaterialId = material.MaterialId,
                    Result = new NestResponse
                    {
                        Success = true,
                        Sheets =
                        [
                            new NestSheet
                            {
                                SheetId = "sheet-1",
                                SheetNumber = 1,
                                MaterialName = material.Name,
                                SheetLength = material.SheetLength,
                                SheetWidth = material.SheetWidth,
                                UtilizationPercent = 6.25m
                            },
                            new NestSheet
                            {
                                SheetId = "sheet-2",
                                SheetNumber = 2,
                                MaterialName = material.Name,
                                SheetLength = material.SheetLength,
                                SheetWidth = material.SheetWidth,
                                UtilizationPercent = 6.25m
                            }
                        ],
                        Placements =
                        [
                            new NestPlacement
                            {
                                PlacementId = "placement-1",
                                SheetId = "sheet-1",
                                PartId = "A-001",
                                Group = "East",
                                X = 0m,
                                Y = 0m,
                                Width = 24m,
                                Height = 12m
                            },
                            new NestPlacement
                            {
                                PlacementId = "placement-2",
                                SheetId = "sheet-2",
                                PartId = "B-001",
                                Group = "West",
                                X = 0m,
                                Y = 0m,
                                Width = 24m,
                                Height = 12m
                            }
                        ],
                        Summary = new MaterialSummary
                        {
                            TotalSheets = 2,
                            TotalPlaced = 2,
                            TotalUnplaced = 0,
                            OverallUtilization = 6.25m
                        }
                    }
                }
            ]
        };

        var project = CreateMinimalProject() with
        {
            MaterialSnapshots = [material],
            State = new ProjectState
            {
                Parts =
                [
                    new PartRow
                    {
                        RowId = "row-1",
                        ImportedId = "A-001",
                        Length = 24m,
                        Width = 12m,
                        Quantity = 1,
                        MaterialName = material.Name,
                        Group = "East",
                        ValidationStatus = ValidationStatuses.Valid
                    },
                    new PartRow
                    {
                        RowId = "row-2",
                        ImportedId = "B-001",
                        Length = 24m,
                        Width = 12m,
                        Quantity = 1,
                        MaterialName = material.Name,
                        Group = "West",
                        ValidationStatus = ValidationStatuses.Valid
                    }
                ],
                LastNestingResult = batchResponse.LegacyResult,
                LastBatchNestingResult = batchResponse
            }
        };

        var response = await DispatchAsync<ExportExcelReportResponse>(
            dispatcher,
            BridgeMessageTypes.ExportExcelReport,
            new ExportExcelReportRequest(project, batchResponse));

        Assert.True(response.Success);
        Assert.Equal(xlsxPath, response.FilePath);
        Assert.True(File.Exists(xlsxPath));
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

    [Fact]
    public async Task Export_pdf_report_opens_the_generated_file_after_saving()
    {
        Directory.CreateDirectory(_workspacePath);

        var pdfPath = Path.Combine(_workspacePath, "opened-report.pdf");
        var dialogs = new RecordingFileDialogService(savePaths: [pdfPath]);
        var launchedFiles = new List<string>();
        var dispatcher = CreateDispatcher(dialogs, new QuestPdfReportExporter(), launchedFiles.Add);
        var project = CreateMinimalProject();

        var response = await DispatchAsync<ExportPdfReportResponse>(
            dispatcher,
            BridgeMessageTypes.ExportPdfReport,
            new ExportPdfReportRequest(project));

        Assert.True(response.Success);
        Assert.Equal(pdfPath, response.FilePath);
        Assert.Single(launchedFiles);
        Assert.Equal(pdfPath, launchedFiles[0]);
        Assert.Contains("opened PDF report", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Export_pdf_report_still_succeeds_when_the_default_viewer_cannot_be_opened()
    {
        Directory.CreateDirectory(_workspacePath);

        var pdfPath = Path.Combine(_workspacePath, "launch-failure-report.pdf");
        var dialogs = new RecordingFileDialogService(savePaths: [pdfPath]);
        var dispatcher = CreateDispatcher(
            dialogs,
            new QuestPdfReportExporter(),
            _ => throw new InvalidOperationException("Viewer unavailable."));
        var project = CreateMinimalProject();

        var response = await DispatchAsync<ExportPdfReportResponse>(
            dispatcher,
            BridgeMessageTypes.ExportPdfReport,
            new ExportPdfReportRequest(project));

        Assert.True(response.Success);
        Assert.Equal(pdfPath, response.FilePath);
        Assert.True(File.Exists(pdfPath));
        Assert.Contains("Could not open it automatically", response.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_pdf_report_succeeds_for_grouped_batch_results()
    {
        Directory.CreateDirectory(_workspacePath);

        var materialFilePath = Path.Combine(_workspacePath, "grouped-export-materials.json");
        var pdfPath = Path.Combine(_workspacePath, "grouped-export-report.pdf");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "mat-grouped-export");
        var projectService = new ProjectService(materialService, idGenerator: () => "project-grouped-export");
        var dialogs = new RecordingFileDialogService(savePaths: [pdfPath]);

        var materialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Grouped Baltic Birch 18mm",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0m,
                DefaultEdgeMargin = 0m
            });
        var material = Assert.IsType<Material>(materialResult.Material);

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
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true),
            exportedPdfOpener: static _ => { });

        PartRow[] parts =
        [
            new()
            {
                RowId = "row-1",
                ImportedId = "A-001",
                Length = 60m,
                Width = 24m,
                Quantity = 3,
                MaterialName = material.Name,
                Group = "A",
                ValidationStatus = ValidationStatuses.Valid
            },
            new()
            {
                RowId = "row-2",
                ImportedId = "B-001",
                Length = 36m,
                Width = 24m,
                Quantity = 5,
                MaterialName = material.Name,
                Group = "B",
                ValidationStatus = ValidationStatuses.Valid
            }
        ];

        var batchResponse = await DispatchAsync<BatchNestResponse>(
            dispatcher,
            BridgeMessageTypes.RunBatchNesting,
            new BatchNestRequest
            {
                Parts = parts,
                Materials = [material],
                KerfWidth = 0m,
                SelectedMaterialId = material.MaterialId
            });

        Assert.True(batchResponse.Success);

        var project = new Project
        {
            ProjectId = "project-grouped-export",
            Metadata = new ProjectMetadata
            {
                ProjectName = "Grouped Export Test",
                ProjectNumber = "PN-GRP"
            },
            Settings = new ProjectSettings
            {
                ReportSettings = new ReportSettings()
            },
            MaterialSnapshots = [material],
            State = new ProjectState
            {
                Parts = parts,
                SelectedMaterialId = material.MaterialId,
                LastNestingResult = batchResponse.LegacyResult,
                LastBatchNestingResult = batchResponse
            }
        };

        var response = await DispatchAsync<ExportPdfReportResponse>(
            dispatcher,
            BridgeMessageTypes.ExportPdfReport,
            new ExportPdfReportRequest(project, batchResponse));

        Assert.True(response.Success, response.Error?.Message);
        Assert.Equal(pdfPath, response.FilePath);
        Assert.True(File.Exists(pdfPath));
    }

    [Fact]
    public async Task Get_stiffener_takeoff_returns_a_preview_for_enabled_projects()
    {
        var dispatcher = CreateDispatcher(
            new RecordingFileDialogService(),
            new QuestPdfReportExporter(),
            stiffenerTakeoffService: new StiffenerTakeoffService(),
            stiffenerPdfReportExporter: new QuestPdfStiffenerReportExporter());
        var project = CreateStiffenerProject();

        var response = await DispatchAsync<GetStiffenerTakeoffResponse>(
            dispatcher,
            BridgeMessageTypes.GetStiffenerTakeoff,
            new GetStiffenerTakeoffRequest(project));

        Assert.True(response.Success);
        Assert.NotNull(response.Report);
        Assert.True(response.Report!.HasTakeoff);
        Assert.Equal(3, response.Report.OverallSummary.EligiblePanelCount);
        Assert.Equal(4, response.Report.OverallSummary.TotalStiffenerCount);
        Assert.Equal("Calculated stiffener takeoff.", response.Message);
    }

    [Fact]
    public async Task Export_stiffener_pdf_report_writes_a_standalone_pdf_file()
    {
        Directory.CreateDirectory(_workspacePath);

        var pdfPath = Path.Combine(_workspacePath, "stiffener-report.pdf");
        var dialogs = new RecordingFileDialogService(savePaths: [pdfPath]);
        var launchedFiles = new List<string>();
        var dispatcher = CreateDispatcher(
            dialogs,
            new QuestPdfReportExporter(),
            exportedPdfOpener: launchedFiles.Add,
            stiffenerTakeoffService: new StiffenerTakeoffService(),
            stiffenerPdfReportExporter: new QuestPdfStiffenerReportExporter());
        var project = CreateStiffenerProject();

        var response = await DispatchAsync<ExportStiffenerPdfReportResponse>(
            dispatcher,
            BridgeMessageTypes.ExportStiffenerPdfReport,
            new ExportStiffenerPdfReportRequest(project));

        Assert.True(response.Success);
        Assert.Equal(pdfPath, response.FilePath);
        Assert.True(File.Exists(pdfPath));
        Assert.Single(launchedFiles);
        Assert.Equal(pdfPath, launchedFiles[0]);
        Assert.Contains("stiffener PDF report", response.Message, StringComparison.OrdinalIgnoreCase);

        var saveRequest = Assert.Single(dialogs.SaveRequests);
        Assert.Equal("Export OptiFab stiffener PDF report", saveRequest.Title);
        Assert.Equal("Export Test Stiffener Takeoff.pdf", saveRequest.FileName);
    }

    [Fact]
    public async Task Export_stiffener_pdf_report_requires_stiffener_takeoff_to_be_enabled()
    {
        Directory.CreateDirectory(_workspacePath);

        var pdfPath = Path.Combine(_workspacePath, "disabled-stiffener-report.pdf");
        var dialogs = new RecordingFileDialogService(savePaths: [pdfPath]);
        var dispatcher = CreateDispatcher(
            dialogs,
            new QuestPdfReportExporter(),
            stiffenerTakeoffService: new StiffenerTakeoffService(),
            stiffenerPdfReportExporter: new QuestPdfStiffenerReportExporter());
        var project = CreateMinimalProject();

        var response = await DispatchAsync<ExportStiffenerPdfReportResponse>(
            dispatcher,
            BridgeMessageTypes.ExportStiffenerPdfReport,
            new ExportStiffenerPdfReportRequest(project));

        Assert.False(response.Success);
        Assert.Equal(pdfPath, response.FilePath);
        Assert.NotNull(response.Error);
        Assert.Equal("stiffener-report-disabled", response.Error!.Code);
        Assert.Single(dialogs.SaveRequests);
        Assert.False(File.Exists(pdfPath));
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

    private static OptimizationGroup StockLengthGroup(
        string id,
        string name,
        int order,
        string profileNumber,
        string finish) =>
        new()
        {
            OptimizationGroupId = id,
            Name = name,
            Order = order,
            ResultStatus = OptimizationResultStatus.Valid,
            LastStockLengthOptimizationResult = new StockLengthOptimizationResult
            {
                OptimizationGroupId = id,
                Status = CutPlanStatus.Complete,
                CutPlans =
                [
                    new CutPlan
                    {
                        StockGroup = new StockGroup { ProfileNumber = profileNumber, Finish = finish },
                        Status = CutPlanStatus.Complete,
                        StockItems =
                        [
                            new StockItem
                            {
                                StockItemNumber = 1,
                                StockLength = 120m,
                                PieceLength = 24m,
                                Remainder = 96m,
                                UtilizationPercent = 20m,
                                CutSequence =
                                [
                                    new PieceInstance
                                    {
                                        PieceInstanceId = $"{id}-piece-1",
                                        RequiredPieceId = $"{id}-required",
                                        InstanceNumber = 1,
                                        Length = 24m,
                                        ProfileNumber = profileNumber,
                                        Finish = finish
                                    }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

    private BridgeMessageDispatcher CreateDispatcher(
        IFileDialogService dialogs,
        IPdfReportExporter pdfReportExporter,
        Action<string>? exportedPdfOpener = null,
        IExcelReportExporter? excelReportExporter = null,
        IStiffenerTakeoffService? stiffenerTakeoffService = null,
        IStiffenerPdfReportExporter? stiffenerPdfReportExporter = null)
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
            excelReportExporter,
            stiffenerTakeoffService,
            stiffenerPdfReportExporter,
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true),
            exportedPdfOpener: exportedPdfOpener ?? (_ => { }));
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

    private static Project CreateStiffenerProject() =>
        CreateMinimalProject() with
        {
            Settings = new ProjectSettings
            {
                ReportSettings = new ReportSettings(),
                StiffenerTakeoff = new StiffenerTakeoffSettings
                {
                    Enabled = true,
                    MinimumLengthInches = 32m,
                    MinimumWidthInches = 32m,
                    WidthDeductionInches = 4m,
                    StockLengthFeet = 20m
                }
            },
            State = new ProjectState
            {
                Parts =
                [
                    new PartRow
                    {
                        RowId = "row-1",
                        ImportedId = "A-100",
                        Length = 32m,
                        Width = 40m,
                        Quantity = 2,
                        MaterialName = "Baltic Birch 18mm",
                        ValidationStatus = ValidationStatuses.Valid
                    },
                    new PartRow
                    {
                        RowId = "row-2",
                        ImportedId = "A-200",
                        Length = 56m,
                        Width = 48m,
                        Quantity = 1,
                        MaterialName = "Maple Ply 18mm",
                        ValidationStatus = ValidationStatuses.Valid
                    },
                    new PartRow
                    {
                        RowId = "row-3",
                        ImportedId = "A-300",
                        Length = 56m,
                        Width = 30m,
                        Quantity = 1,
                        MaterialName = "Maple Ply 18mm",
                        ValidationStatus = ValidationStatuses.Valid
                    }
                ]
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
