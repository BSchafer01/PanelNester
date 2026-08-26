using System.IO;
using System.Text.Json;
using ClosedXML.Excel;
using PanelNester.Desktop.Bridge;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;
using PanelNester.Services.Materials;
using PanelNester.Services.Nesting;
using PanelNester.Services.Projects;
using PanelNester.Services.Reporting;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class DesktopBridgeRoundTripSpecs : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = BridgeJson.SerializerOptions;
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.DesktopBridgeRoundTripSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task File_open_import_and_nesting_share_one_live_vertical_slice()
    {
        var csvPath = Path.Combine(_workspacePath, "parts.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "baltic-birch-18");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new StubFileDialogService(csvPath),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-roundtrip"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        try
        {
            var createMaterialResponse = await DispatchAsync<CreateMaterialResponse>(
                dispatcher,
                BridgeMessageTypes.CreateMaterial,
                new CreateMaterialRequest(
                    new Material
                    {
                        Name = "Baltic Birch 18mm",
                        SheetLength = 96m,
                        SheetWidth = 48m,
                        AllowRotation = true,
                        DefaultSpacing = 0.125m,
                        DefaultEdgeMargin = 0.5m,
                        ColorFinish = "Natural"
                    }));
            Assert.True(createMaterialResponse.Success);
            var material = Assert.IsType<Material>(createMaterialResponse.Material);

            await File.WriteAllTextAsync(
                csvPath,
                $$"""
                Id,Length,Width,Quantity,Material
                P-001,20,10,1,{{material.Name}}
                """);

            var dialogResponse = await DispatchAsync<OpenFileDialogResponse>(
                dispatcher,
                BridgeMessageTypes.OpenFileDialog,
                new OpenFileDialogRequest("Select a CSV file", [new FileDialogFilter("CSV files", ["csv"])]));
            Assert.True(dialogResponse.Success);
            Assert.Equal(csvPath, dialogResponse.FilePath);

            var importResponse = await DispatchAsync<ImportResponse>(
                dispatcher,
                BridgeMessageTypes.ImportCsv,
                new ImportRequest { FilePath = dialogResponse.FilePath! });
            Assert.True(importResponse.Success);
            var importedPart = Assert.Single(importResponse.Parts);
            Assert.Equal("P-001", importedPart.ImportedId);

            var nestResponse = await DispatchAsync<NestResponse>(
                dispatcher,
                BridgeMessageTypes.RunNesting,
                new NestRequest
                {
                    Parts = importResponse.Parts,
                    Material = material,
                    KerfWidth = 0.0625m
                });

            Assert.True(nestResponse.Success);
            Assert.Single(nestResponse.Sheets);
            Assert.Single(nestResponse.Placements);
            Assert.Equal(1, nestResponse.Summary.TotalPlaced);
            Assert.Empty(nestResponse.UnplacedItems);
        }
        finally
        {
            if (File.Exists(csvPath))
            {
                File.Delete(csvPath);
            }
        }
    }

    [Fact]
    public async Task Grouped_import_edit_and_batch_nesting_share_one_live_vertical_slice()
    {
        var csvPath = Path.Combine(_workspacePath, "grouped-parts.csv");
        var materialFilePath = Path.Combine(_workspacePath, "grouped-materials.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "grouped-birch-18");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new StubFileDialogService(csvPath),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-grouped-roundtrip"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            new BatchNestingService(new ShelfNestingService()),
            new ReportDataService(),
            new QuestPdfReportExporter(),
            new ClosedXmlExcelReportExporter(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true),
            exportedPdfOpener: static _ => { });

        try
        {
            var createMaterialResponse = await DispatchAsync<CreateMaterialResponse>(
                dispatcher,
                BridgeMessageTypes.CreateMaterial,
                new CreateMaterialRequest(
                    new Material
                    {
                        Name = "Grouped Baltic Birch 18mm",
                        SheetLength = 96m,
                        SheetWidth = 48m,
                        AllowRotation = true,
                        DefaultSpacing = 0m,
                        DefaultEdgeMargin = 0m,
                        ColorFinish = "Natural"
                    }));
            Assert.True(createMaterialResponse.Success);
            var material = Assert.IsType<Material>(createMaterialResponse.Material);

            await File.WriteAllTextAsync(
                csvPath,
                $$"""
                Id,Length,Width,Quantity,Material,Group
                B-001,96,24,1,{{material.Name}},Batch B
                U-001,96,24,1,{{material.Name}},Batch C
                A-001,96,24,1,{{material.Name}},Batch A
                B-002,96,24,1,{{material.Name}},Batch B
                """);

            var importResponse = await DispatchAsync<ImportFileResponse>(
                dispatcher,
                BridgeMessageTypes.ImportFile,
                new ImportFileRequest { FilePath = csvPath });

            Assert.True(importResponse.Success);
            Assert.Equal("Batch C", importResponse.Parts[1].Group);

            var updatedResponse = await DispatchAsync<ImportResponse>(
                dispatcher,
                BridgeMessageTypes.UpdatePartRow,
                new UpdatePartRowRequest
                {
                    Parts = importResponse.Parts,
                    Part = new PartRowUpdate
                    {
                        RowId = importResponse.Parts[1].RowId,
                        ImportedId = importResponse.Parts[1].ImportedId,
                        Length = importResponse.Parts[1].LengthText ?? importResponse.Parts[1].Length.ToString(),
                        Width = importResponse.Parts[1].WidthText ?? importResponse.Parts[1].Width.ToString(),
                        Quantity = importResponse.Parts[1].QuantityText ?? importResponse.Parts[1].Quantity.ToString(),
                        MaterialName = importResponse.Parts[1].MaterialName,
                        Group = string.Empty
                    }
                });

            Assert.True(updatedResponse.Success);
            Assert.Null(updatedResponse.Parts[1].Group);

            var batchResponse = await DispatchAsync<BatchNestResponse>(
                dispatcher,
                BridgeMessageTypes.RunBatchNesting,
                new BatchNestRequest
                {
                    Parts = updatedResponse.Parts,
                    Materials = [material],
                    KerfWidth = 0m,
                    SelectedMaterialId = material.MaterialId
                });

            Assert.True(batchResponse.Success);
            var materialResult = Assert.Single(batchResponse.MaterialResults);
            Assert.Equal(
                ["B-001", "B-002", "A-001", "U-001"],
                materialResult.Result.Placements.Select(placement => placement.PartId).ToArray());
            Assert.Equal(
                new string?[] { "Batch B", "Batch B", "Batch A", null },
                materialResult.Result.Placements.Select(placement => placement.Group).ToArray());
            Assert.Equal(materialResult.Result, batchResponse.LegacyResult);
        }
        finally
        {
            if (File.Exists(csvPath))
            {
                File.Delete(csvPath);
            }
        }
    }

    [Fact]
    public async Task Run_all_round_trip_preserves_group_isolation_order_and_result_identities()
    {
        var materialFilePath = Path.Combine(_workspacePath, "run-all-materials.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository);
        var validator = new PartRowValidator();
        var nestingService = new ShelfNestingService();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new StubFileDialogService(_workspacePath),
            materialService,
            new ProjectService(materialService),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            nestingService,
            new BatchNestingService(nestingService, () => "bridge-run-001"),
            new ReportDataService(),
            new QuestPdfReportExporter(),
            new ClosedXmlExcelReportExporter(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));
        var material = new Material
        {
            MaterialId = "mat-maple",
            Name = "Maple",
            SheetLength = 96m,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = 0m,
            DefaultEdgeMargin = 0m
        };

        var response = await DispatchAsync<BatchNestResponse>(
            dispatcher,
            BridgeMessageTypes.RunBatchNesting,
            new BatchNestRequest
            {
                OptimizationGroups =
                [
                    CreateRunGroup("group-b", "Group B", 1, "B-001", material.Name),
                    CreateRunGroup("group-a", "Group A", 0, "A-001", material.Name)
                ],
                Materials = [material],
                KerfWidth = 0m
            });

        Assert.True(response.Success);
        Assert.Equal("bridge-run-001", response.ExecutionId);
        Assert.Equal(["group-a", "group-b"],
            response.OptimizationGroupResults.Select(result => result.OptimizationGroupId));
        Assert.Equal(2, response.OptimizationGroupResults
            .SelectMany(group => group.MaterialResults)
            .SelectMany(result => result.Result.Sheets)
            .Select(sheet => sheet.SheetId)
            .Distinct(StringComparer.Ordinal)
            .Count());
        Assert.All(response.OptimizationGroupResults, group =>
        {
            var materialResult = Assert.Single(group.MaterialResults);
            Assert.All(materialResult.Result.Sheets,
                sheet => Assert.StartsWith(group.OptimizationResultId, sheet.SheetId, StringComparison.Ordinal));
            Assert.All(materialResult.Result.Placements,
                placement => Assert.StartsWith(group.OptimizationResultId, placement.PlacementId, StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task Multi_Worksheet_import_round_trips_through_save_reopen_grouped_optimization_and_provenance()
    {
        Directory.CreateDirectory(_workspacePath);
        var workbookPath = Path.Combine(_workspacePath, "multi-worksheet.xlsx");
        var projectPath = Path.Combine(_workspacePath, "multi-worksheet.pnest");
        var repository = new JsonMaterialRepository(Path.Combine(_workspacePath, "multi-worksheet-materials.json"));
        var materialService = new MaterialService(repository);
        Assert.Contains(await materialService.ListAsync(), material => material.Name == DemoMaterialCatalog.Phase1.Name);

        using (var workbook = new XLWorkbook())
        {
            WriteWorksheet(workbook.AddWorksheet("East"), "P-001", 1);
            WriteWorksheet(workbook.AddWorksheet("West"), "P-001", 2);
            WriteWorksheet(workbook.AddWorksheet("Service"), "P-002", 1);
            workbook.SaveAs(workbookPath);
        }

        var nestingService = new ShelfNestingService();
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new StubFileDialogService(workbookPath),
            materialService,
            new ProjectService(materialService, idGenerator: () => "multi-worksheet-project"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            nestingService,
            new BatchNestingService(nestingService, () => "multi-worksheet-run"),
            new ReportDataService(),
            new QuestPdfReportExporter(),
            new ClosedXmlExcelReportExporter(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        const string sessionId = "multi-worksheet-round-trip";
        var begun = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = workbookPath });
        Assert.True(begun.Success);
        var east = await PreviewWorksheetAsync(dispatcher, sessionId, "East", 1, "facades", "Facades");
        var west = await PreviewWorksheetAsync(dispatcher, sessionId, "West", 2, "facades", "Facades");
        var service = await PreviewWorksheetAsync(dispatcher, sessionId, "Service", 3, "service", "Service");

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project
                {
                    ProjectId = "multi-worksheet-project",
                    Metadata = new ProjectMetadata { ProjectName = "Multi Worksheet" }
                },
                Worksheets =
                [
                    east,
                    west,
                    service
                ]
            });
        Assert.True(finalized.Success);

        var saved = await DispatchAsync<SaveProjectResponse>(
            dispatcher,
            BridgeMessageTypes.SaveProject,
            new SaveProjectRequest(finalized.Project!, projectPath));
        Assert.True(saved.Success);

        var reopened = await DispatchAsync<OpenProjectResponse>(
            dispatcher,
            BridgeMessageTypes.OpenProject,
            new OpenProjectRequest(projectPath));
        Assert.True(reopened.Success);
        var project = Assert.IsType<Project>(reopened.Project);
        Assert.Equal(3, project.State.ImportConfiguration?.Worksheets.Count);
        Assert.Equal(["East", "West", "Service"],
            project.State.ImportConfiguration?.Worksheets.Select(worksheet => worksheet.WorksheetName));
        Assert.All(project.State.ImportConfiguration!.Worksheets, worksheet =>
        {
            Assert.Equal("A1:E1", worksheet.HeadingRange);
            Assert.Equal(5, worksheet.ColumnMappings.Count);
            Assert.False(string.IsNullOrWhiteSpace(worksheet.OptimizationGroupId));
        });
        var materialResolution = Assert.Single(project.State.ImportConfiguration.Options.MaterialMappings);
        Assert.Equal(DemoMaterialCatalog.Phase1.Name, materialResolution.SourceMaterialName);
        Assert.Equal(DemoMaterialCatalog.Phase1.MaterialId, materialResolution.TargetMaterialId);
        Assert.DoesNotContain(
            typeof(ProjectState).GetProperties(),
            property => property.PropertyType == typeof(byte[]));

        var facades = Assert.Single(
            project.State.OptimizationGroups,
            group => group.OptimizationGroupId == "facades");
        var combined = Assert.Single(facades.Parts);
        Assert.Equal(3, combined.Quantity);
        Assert.Equal(
            ["East!2", "West!2"],
            combined.SourceReferences.Select(reference => $"{reference.WorksheetName}!{reference.PhysicalRow}"));

        var run = await DispatchAsync<BatchNestResponse>(
            dispatcher,
            BridgeMessageTypes.RunBatchNesting,
            new BatchNestRequest
            {
                OptimizationGroups = project.State.OptimizationGroups.Select(group => new OptimizationGroupNestRequest
                {
                    OptimizationGroupId = group.OptimizationGroupId,
                    Name = group.Name,
                    Order = group.Order,
                    OwnedPartRowIds = group.Parts.Select(part => part.RowId).ToArray(),
                    Parts = group.Parts
                }).ToArray(),
                Materials = project.MaterialSnapshots,
                KerfWidth = project.Settings.KerfWidth
            });
        Assert.True(run.Success);
        Assert.Equal(["facades", "service"],
            run.OptimizationGroupResults.Select(group => group.OptimizationGroupId));
        Assert.All(run.OptimizationGroupResults, group =>
            Assert.All(group.MaterialResults.SelectMany(material => material.Result.Placements), placement =>
                Assert.StartsWith(group.OptimizationResultId, placement.PlacementId, StringComparison.Ordinal)));
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

    private static OptimizationGroupNestRequest CreateRunGroup(
        string id,
        string name,
        int order,
        string partId,
        string materialName) =>
        new()
        {
            OptimizationGroupId = id,
            Name = name,
            Order = order,
            Parts =
            [
                new PartRow
                {
                    RowId = partId,
                    ImportedId = partId,
                    Length = 24m,
                    Width = 12m,
                    Quantity = 1,
                    MaterialName = materialName,
                    ValidationStatus = ValidationStatuses.Valid
                }
            ]
        };

    private static async Task<ImportWorksheetSelection> PreviewWorksheetAsync(
        BridgeMessageDispatcher dispatcher,
        string sessionId,
        string worksheetName,
        int originalPosition,
        string optimizationGroupId,
        string optimizationGroupName)
    {
        var options = new ImportOptions
        {
            ColumnMappings =
            [
                new ImportColumnMapping { SourceColumn = "A", TargetField = ImportFieldNames.Id },
                new ImportColumnMapping { SourceColumn = "B", TargetField = ImportFieldNames.Length },
                new ImportColumnMapping { SourceColumn = "C", TargetField = ImportFieldNames.Width },
                new ImportColumnMapping { SourceColumn = "D", TargetField = ImportFieldNames.Quantity },
                new ImportColumnMapping { SourceColumn = "E", TargetField = ImportFieldNames.Material }
            ]
        };
        var preview = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest
            {
                SessionId = sessionId,
                WorksheetName = worksheetName,
                HeadingRange = "A1:E1",
                Options = options
            });
        Assert.True(preview.Success);
        Assert.Equal(originalPosition, preview.Worksheet?.OriginalPosition);
        return new ImportWorksheetSelection
        {
            WorksheetName = worksheetName,
            OriginalPosition = originalPosition,
            HeadingRange = "A1:E1",
            Options = options,
            OptimizationGroupId = optimizationGroupId,
            OptimizationGroupName = optimizationGroupName
        };
    }

    private static void WriteWorksheet(IXLWorksheet worksheet, string partId, int quantity)
    {
        string[] headings = ["Id", "Length", "Width", "Quantity", "Material"];
        for (var column = 0; column < headings.Length; column++)
        {
            worksheet.Cell(1, column + 1).Value = headings[column];
        }

        worksheet.Cell(2, 1).Value = partId;
        worksheet.Cell(2, 2).Value = 20;
        worksheet.Cell(2, 3).Value = 10;
        worksheet.Cell(2, 4).Value = quantity;
        worksheet.Cell(2, 5).Value = DemoMaterialCatalog.Phase1.Name;
    }

    private sealed class StubFileDialogService(string filePath) : IFileDialogService
    {
        public Task<OpenFileDialogResponse> OpenAsync(
            OpenFileDialogRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new OpenFileDialogResponse(true, filePath, null, "File selected."));

        public Task<SaveFileDialogResponse> SaveAsync(
            SaveFileDialogRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SaveFileDialogResponse(true, filePath, null, "File path selected."));
    }
}
