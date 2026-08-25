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

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class ImportBridgeSpecs : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = BridgeJson.SerializerOptions;
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.ImportBridgeSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Import_file_message_uses_native_filters_and_routes_csv_and_xlsx_files()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "parts.csv");
        var xlsxPath = Path.Combine(_workspacePath, "parts.xlsx");
        var materialFilePath = Path.Combine(_workspacePath, "materials.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "baltic-birch-18");
        var validator = new PartRowValidator();
        var dialogs = new RecordingFileDialogService(openPaths: [csvPath]);
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            dialogs,
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-import-bridge"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var createMaterialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Baltic Birch 18mm",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createMaterialResult.Success);
        var material = Assert.IsType<Material>(createMaterialResult.Material);

        await File.WriteAllTextAsync(
            csvPath,
            $$"""
            Id,Length,Width,Quantity,Material
            P-001,20,10,1,{{material.Name}}
            """);
        WriteWorkbook(xlsxPath, material.Name);

        var csvResponse = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest());

        Assert.True(csvResponse.Success);
        Assert.Equal(csvPath, csvResponse.FilePath);
        Assert.Single(csvResponse.Parts);
        Assert.Equal(ImportFieldNames.Required, csvResponse.AvailableColumns);
        Assert.Equal(ImportFieldNames.All.Count, csvResponse.ColumnMappings.Count);
        Assert.All(
            ImportFieldNames.All,
            targetField => Assert.Contains(
                csvResponse.ColumnMappings,
                mapping => mapping.TargetField == targetField));
        Assert.Contains(
            csvResponse.ColumnMappings,
            mapping => mapping.TargetField == ImportFieldNames.Group && mapping.SourceColumn is null);
        Assert.Equal("P-001", csvResponse.Parts[0].ImportedId);
        Assert.Equal("20", csvResponse.Parts[0].LengthText);
        Assert.Equal("10", csvResponse.Parts[0].WidthText);
        Assert.Equal("1", csvResponse.Parts[0].QuantityText);
        Assert.Contains(csvResponse.MaterialResolutions, resolution =>
            resolution.SourceMaterialName == material.Name &&
            resolution.Status == ImportMaterialResolutionStatuses.Resolved);

        var dialogRequest = Assert.Single(dialogs.OpenRequests);
        Assert.Contains(dialogRequest.Filters!, filter => filter.Extensions.Contains("csv", StringComparer.Ordinal));
        Assert.Contains(dialogRequest.Filters!, filter => filter.Extensions.Contains("xlsx", StringComparer.Ordinal));

        var xlsxResponse = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest { FilePath = xlsxPath });

        Assert.True(xlsxResponse.Success);
        Assert.Equal(xlsxPath, xlsxResponse.FilePath);
        var csvPart = Assert.Single(csvResponse.Parts);
        var xlsxPart = Assert.Single(xlsxResponse.Parts);
        Assert.Equal(csvPart.ImportedId, xlsxPart.ImportedId);
        Assert.Equal(csvPart.Length, xlsxPart.Length);
        Assert.Equal(csvPart.Width, xlsxPart.Width);
        Assert.Equal(csvPart.Quantity, xlsxPart.Quantity);
        Assert.Equal(csvPart.MaterialName, xlsxPart.MaterialName);
        Assert.Equal(csvResponse.Errors, xlsxResponse.Errors);
        Assert.Equal(csvResponse.Warnings, xlsxResponse.Warnings);
        Assert.Equal(csvResponse.AvailableColumns, xlsxResponse.AvailableColumns);
        Assert.Equal(csvResponse.ColumnMappings, xlsxResponse.ColumnMappings);
        Assert.Equal(csvResponse.MaterialResolutions, xlsxResponse.MaterialResolutions);
    }

    [Fact]
    public async Task Import_session_uses_one_immutable_snapshot_for_later_previews()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "snapshot-parts.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-snapshot.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "snapshot-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-snapshot"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var materialResult = await materialService.CreateAsync(new Material
        {
            Name = "Snapshot Material",
            SheetLength = 96m,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = 0.125m,
            DefaultEdgeMargin = 0.5m
        });
        Assert.True(materialResult.Success);

        await File.WriteAllTextAsync(
            csvPath,
            "Id,Length,Width,Quantity,Material\nORIGINAL,20,10,1,Snapshot Material\n");

        const string sessionId = "immutable-snapshot-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = csvPath });

        Assert.True(started.Success);
        Assert.Empty(started.Parts);

        await File.WriteAllTextAsync(
            csvPath,
            "Id,Length,Width,Quantity,Material\nCHANGED,99,88,1,Snapshot Material\n");

        var preview = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest { SessionId = sessionId });

        Assert.True(preview.Success);
        Assert.Equal(csvPath, preview.ImportSourcePath);
        var row = Assert.Single(preview.Parts);
        Assert.Equal("ORIGINAL", row.ImportedId);
        Assert.Equal(20m, row.Length);
        Assert.Equal(10m, row.Width);
    }

    [Fact]
    public async Task Import_session_retains_single_worksheet_excel_behavior_after_the_source_is_removed()
    {
        Directory.CreateDirectory(_workspacePath);

        var xlsxPath = Path.Combine(_workspacePath, "snapshot-parts.xlsx");
        var repository = new JsonMaterialRepository(Path.Combine(_workspacePath, "xlsx-session-materials.json"));
        var materialService = new MaterialService(repository, idGenerator: () => "xlsx-session-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-xlsx-session"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        await materialService.CreateAsync(new Material
        {
            Name = "Excel Snapshot Material",
            SheetLength = 96m,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = 0.125m,
            DefaultEdgeMargin = 0.5m
        });
        WriteWorkbook(xlsxPath, "Excel Snapshot Material");

        const string sessionId = "xlsx-snapshot-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = xlsxPath });
        Assert.True(started.Success);

        File.Delete(xlsxPath);
        var preview = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest { SessionId = sessionId });

        Assert.True(preview.Success);
        Assert.Equal(xlsxPath, preview.ImportSourcePath);
        var excelPart = Assert.Single(preview.Parts);
        Assert.Equal("P-001", excelPart.ImportedId);
        var excelSource = Assert.Single(excelPart.SourceReferences);
        Assert.Equal("Parts", excelSource.WorksheetName);
        Assert.Equal(2, excelSource.PhysicalRow);
        Assert.False(string.IsNullOrWhiteSpace(excelSource.SourceFingerprint));

        var cancelled = await DispatchAsync<CancelImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.CancelImportSession,
            new CancelImportSessionRequest { SessionId = sessionId });
        Assert.True(cancelled.Released);
    }

    [Fact]
    public async Task Import_session_finalization_atomically_returns_the_updated_project_and_releases_the_snapshot()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "finalized-parts.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-finalized.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "finalized-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-finalized"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        await materialService.CreateAsync(new Material
        {
            Name = "Finalized Material",
            SheetLength = 96m,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = 0.125m,
            DefaultEdgeMargin = 0.5m
        });
        await File.WriteAllTextAsync(
            csvPath,
            "Id,Length,Width,Quantity,Material\nNEW-PART,20,10,1,Finalized Material\n");

        var oldPart = new PartRow
        {
            RowId = "row-1",
            ImportedId = "OLD-PART",
            Length = 12m,
            Width = 6m,
            Quantity = 1,
            MaterialName = "Finalized Material"
        };
        var project = new Project
        {
            ProjectId = "atomic-project",
            State = new ProjectState
            {
                SourceFilePath = "old.csv",
                Parts = [oldPart],
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "group-1",
                        Name = "Primary",
                        Order = 0,
                        Parts = [oldPart],
                        LastNestingResult = new NestResponse { Success = true },
                        ResultStatus = OptimizationResultStatus.Valid
                    }
                ],
                LastNestingResult = new NestResponse { Success = true }
            }
        };

        const string sessionId = "atomic-finalization-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = csvPath });
        Assert.True(started.Success);

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = project,
                TargetOptimizationGroupId = "group-1"
            });

        Assert.True(finalized.Success);
        Assert.True(finalized.Finalized);
        var finalizedProject = Assert.IsType<Project>(finalized.Project);
        Assert.Equal(csvPath, finalizedProject.State.SourceFilePath);
        Assert.Equal(csvPath, finalizedProject.State.ImportSource?.ImportSourcePath);
        Assert.False(string.IsNullOrWhiteSpace(finalizedProject.State.ImportSource?.ContentFingerprint));
        Assert.True(finalizedProject.State.ImportSource?.ContentLength > 0);
        Assert.NotNull(finalizedProject.State.ImportConfiguration);
        var finalizedPart = Assert.Single(finalizedProject.State.Parts);
        Assert.Equal("NEW-PART", finalizedPart.ImportedId);
        var sourceReference = Assert.Single(finalizedPart.SourceReferences);
        Assert.Equal(Path.GetFileName(csvPath), sourceReference.WorksheetName);
        Assert.Equal(2, sourceReference.PhysicalRow);
        Assert.False(string.IsNullOrWhiteSpace(sourceReference.SourceFingerprint));
        var worksheetConfiguration = Assert.Single(finalizedProject.State.ImportConfiguration!.Worksheets);
        Assert.Equal(Path.GetFileName(csvPath), worksheetConfiguration.WorksheetName);
        Assert.Equal("R1C1:R1C5", worksheetConfiguration.HeadingRange);
        Assert.Equal("group-1", worksheetConfiguration.OptimizationGroupId);
        Assert.Equal(5, worksheetConfiguration.ColumnMappings.Count);
        Assert.Empty(worksheetConfiguration.ExcludedSourceRows);
        var finalizedGroup = Assert.Single(finalizedProject.State.OptimizationGroups);
        Assert.Equal("NEW-PART", Assert.Single(finalizedGroup.Parts).ImportedId);
        Assert.Null(finalizedGroup.LastNestingResult);
        Assert.Equal(OptimizationResultStatus.None, finalizedGroup.ResultStatus);

        Assert.Equal("OLD-PART", Assert.Single(project.State.Parts).ImportedId);
        Assert.NotNull(Assert.Single(project.State.OptimizationGroups).LastNestingResult);

        var afterFinalization = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest { SessionId = sessionId });
        Assert.False(afterFinalization.Success);
        Assert.Equal("import-session-not-found", afterFinalization.Error?.Code);
    }

    [Fact]
    public async Task Failed_import_session_finalization_cannot_replace_existing_project_state()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "invalid-finalization.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-invalid-finalization.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "invalid-finalization-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-invalid-finalization"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        await File.WriteAllTextAsync(
            csvPath,
            "Id,Length,Width,Quantity,Material\nINVALID,not-a-number,10,1,Unknown\n");

        var oldPart = new PartRow { RowId = "old", ImportedId = "UNCHANGED" };
        var oldResult = new NestResponse { Success = true };
        var project = new Project
        {
            ProjectId = "unchanged-project",
            State = new ProjectState
            {
                SourceFilePath = "existing.csv",
                Parts = [oldPart],
                OptimizationGroups =
                [
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "group-1",
                        Name = "Primary",
                        Parts = [oldPart],
                        LastNestingResult = oldResult,
                        ResultStatus = OptimizationResultStatus.Valid
                    }
                ],
                LastNestingResult = oldResult
            }
        };

        const string sessionId = "failed-finalization-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = csvPath });
        Assert.True(started.Success);

        var finalized = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = project,
                TargetOptimizationGroupId = "group-1",
                NewMaterials =
                [
                    new ImportNewMaterialRequest
                    {
                        SourceMaterialName = "Unknown",
                        Material = new Material
                        {
                            Name = "Temporary Material",
                            SheetLength = 96m,
                            SheetWidth = 48m
                        }
                    }
                ]
            });

        Assert.False(finalized.Success);
        Assert.False(finalized.Finalized);
        Assert.Null(finalized.Project);
        Assert.Equal("UNCHANGED", Assert.Single(project.State.Parts).ImportedId);
        Assert.Same(oldResult, Assert.Single(project.State.OptimizationGroups).LastNestingResult);
        Assert.DoesNotContain(await materialService.ListAsync(), material => material.Name == "Temporary Material");

        var afterFailure = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest { SessionId = sessionId });
        Assert.Equal("import-session-not-found", afterFailure.Error?.Code);
    }

    [Fact]
    public async Task Cancellation_requested_before_begin_prevents_a_late_session_from_becoming_active()
    {
        Directory.CreateDirectory(_workspacePath);
        var csvPath = Path.Combine(_workspacePath, "late-begin.csv");
        await File.WriteAllTextAsync(csvPath, "Id\nP-001\n");

        var materialService = new MaterialService(
            new JsonMaterialRepository(Path.Combine(_workspacePath, "late-begin-materials.json")));
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService),
            new FileImportDispatcher(
                new CsvImportService(DemoMaterialCatalog.All, new PartRowValidator()),
                new XlsxImportService(DemoMaterialCatalog.All, new PartRowValidator())),
            new PartEditorService(DemoMaterialCatalog.All),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        const string sessionId = "cancel-before-begin-session";
        var cancelled = await DispatchAsync<CancelImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.CancelImportSession,
            new CancelImportSessionRequest { SessionId = sessionId });
        var lateBegin = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = csvPath });

        Assert.True(cancelled.Success);
        Assert.False(cancelled.Released);
        Assert.False(lateBegin.Success);
        Assert.Equal("cancelled", lateBegin.Error?.Code);

        var preview = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest { SessionId = sessionId });
        Assert.Equal("import-session-not-found", preview.Error?.Code);
    }

    [Fact]
    public async Task Cancelling_an_import_session_propagates_to_host_work_and_releases_the_snapshot()
    {
        Directory.CreateDirectory(_workspacePath);
        var csvPath = Path.Combine(_workspacePath, "cancelled-session.csv");
        await File.WriteAllTextAsync(csvPath, "Id\nP-001\n");

        var blockingImport = new BlockingImportService();
        var materialService = new MaterialService(
            new JsonMaterialRepository(Path.Combine(_workspacePath, "cancel-materials.json")));
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-cancel-session"),
            blockingImport,
            new PartEditorService(DemoMaterialCatalog.All),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        const string sessionId = "cancelled-import-session";
        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = csvPath });
        Assert.True(started.Success);

        var previewTask = DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest { SessionId = sessionId });
        await blockingImport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var cancelled = await DispatchAsync<CancelImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.CancelImportSession,
            new CancelImportSessionRequest { SessionId = sessionId });
        var latePreviewResponse = await previewTask;

        Assert.True(cancelled.Success);
        Assert.True(cancelled.Released);
        Assert.True(await blockingImport.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(latePreviewResponse.Success);
        Assert.Equal("cancelled", latePreviewResponse.Error?.Code);

        var afterCancellation = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.PreviewImportSession,
            new PreviewImportSessionRequest { SessionId = sessionId });
        Assert.Equal("import-session-not-found", afterCancellation.Error?.Code);
    }

    [Fact]
    public async Task Cancelling_finalization_reaches_material_preparation_and_releases_the_snapshot()
    {
        Directory.CreateDirectory(_workspacePath);
        var csvPath = Path.Combine(_workspacePath, "cancelled-preparation.csv");
        await File.WriteAllTextAsync(csvPath, "Id,Length,Width,Quantity,Material\nP-001,20,10,1,New Material\n");

        var materialService = new BlockingMaterialService();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService),
            new BlockingImportService(),
            new PartEditorService(DemoMaterialCatalog.All),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));
        const string sessionId = "cancel-material-preparation-session";

        var started = await DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.BeginImportSession,
            new BeginImportSessionRequest { SessionId = sessionId, ImportSourcePath = csvPath });
        Assert.True(started.Success);

        var finalizationTask = DispatchAsync<ImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.FinalizeImportSession,
            new FinalizeImportSessionRequest
            {
                SessionId = sessionId,
                Project = new Project(),
                NewMaterials =
                [
                    new ImportNewMaterialRequest
                    {
                        SourceMaterialName = "New Material",
                        Material = new Material { Name = "New Material", SheetLength = 96m, SheetWidth = 48m }
                    }
                ]
            });
        await materialService.CreateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var cancelled = await DispatchAsync<CancelImportSessionResponse>(
            dispatcher,
            BridgeMessageTypes.CancelImportSession,
            new CancelImportSessionRequest { SessionId = sessionId });
        var finalization = await finalizationTask;

        Assert.True(cancelled.Released);
        Assert.True(await materialService.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(finalization.Success);
        Assert.Equal("cancelled", finalization.Error?.Code);
    }

    [Fact]
    public async Task Part_row_edit_messages_return_full_revalidated_import_responses()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "editable-parts.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-edit.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "edit-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-edit-bridge"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var createMaterialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Edit Material",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createMaterialResult.Success);
        var material = Assert.IsType<Material>(createMaterialResult.Material);

        await File.WriteAllTextAsync(
            csvPath,
            $$"""
            Id,Length,Width,Quantity,Material
            P-001,20,10,1,{{material.Name}}
            """);

        var imported = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest { FilePath = csvPath });

        Assert.True(imported.Success);
        Assert.Single(imported.Parts);

        var afterAdd = await DispatchAsync<ImportResponse>(
            dispatcher,
            BridgeMessageTypes.AddPartRow,
            new AddPartRowRequest
            {
                Parts = imported.Parts,
                Part = new PartRowUpdate
                {
                    ImportedId = "P-001",
                    Length = "18",
                    Width = "30",
                    Quantity = "1",
                    MaterialName = material.Name
                }
            });

        Assert.True(afterAdd.Success);
        Assert.Equal(2, afterAdd.Parts.Count);
        Assert.Equal("row-2", afterAdd.Parts[1].RowId);
        Assert.Equal("18", afterAdd.Parts[1].LengthText);
        var duplicateWarning = Assert.Single(afterAdd.Warnings);
        Assert.Equal("duplicate-id", duplicateWarning.Code);
        Assert.Equal("row-2", duplicateWarning.RowId);

        var afterUpdate = await DispatchAsync<ImportResponse>(
            dispatcher,
            BridgeMessageTypes.UpdatePartRow,
            new UpdatePartRowRequest
            {
                Parts = afterAdd.Parts,
                Part = new PartRowUpdate
                {
                    RowId = "row-2",
                    ImportedId = "P-001",
                    Length = "18",
                    Width = "30",
                    Quantity = "oops",
                    MaterialName = material.Name
                }
            });

        Assert.False(afterUpdate.Success);
        Assert.Equal(2, afterUpdate.Parts.Count);
        Assert.Equal("oops", afterUpdate.Parts[1].QuantityText);
        Assert.Contains(afterUpdate.Errors, error => error.Code == "invalid-quantity" && error.RowId == "row-2");
        Assert.Contains(afterUpdate.Warnings, warning => warning.Code == "duplicate-id" && warning.RowId == "row-2");

        var afterDelete = await DispatchAsync<ImportResponse>(
            dispatcher,
            BridgeMessageTypes.DeletePartRow,
            new DeletePartRowRequest
            {
                Parts = afterUpdate.Parts,
                RowId = "row-2"
            });

        Assert.True(afterDelete.Success);
        Assert.Single(afterDelete.Parts);
        Assert.Empty(afterDelete.Errors);
        Assert.Empty(afterDelete.Warnings);
    }

    [Fact]
    public async Task Import_file_request_can_apply_user_defined_column_mappings()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "mapped-columns.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-mapped-columns.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "mapped-columns-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-mapped-columns"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var createMaterialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Mapped Columns Material",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createMaterialResult.Success);
        var material = Assert.IsType<Material>(createMaterialResult.Material);

        await File.WriteAllTextAsync(
            csvPath,
            $$"""
            Part Id,Len,Width,Qty,Sheet Material
            P-001,20,10,1,{{material.Name}}
            """);

        var response = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest
            {
                FilePath = csvPath,
                Options = new ImportOptions
                {
                    ColumnMappings =
                    [
                        new ImportColumnMapping { SourceColumn = "Part Id", TargetField = ImportFieldNames.Id },
                        new ImportColumnMapping { SourceColumn = "Len", TargetField = ImportFieldNames.Length },
                        new ImportColumnMapping { SourceColumn = "Qty", TargetField = ImportFieldNames.Quantity },
                        new ImportColumnMapping { SourceColumn = "Sheet Material", TargetField = ImportFieldNames.Material }
                    ]
                }
            });

        Assert.True(response.Success);
        var row = Assert.Single(response.Parts);
        Assert.Equal("P-001", row.ImportedId);
        Assert.Equal(material.Name, row.MaterialName);
        Assert.Contains(response.ColumnMappings, mapping => mapping.TargetField == ImportFieldNames.Id && mapping.SourceColumn == "Part Id");
    }

    [Fact]
    public async Task Import_file_request_returns_an_actionable_error_when_the_selected_file_is_locked()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "locked.csv");
        await File.WriteAllTextAsync(
            csvPath,
            """
            Id,Length,Width,Quantity,Material
            P-001,20,10,1,Demo Material
            """);

        var materialFilePath = Path.Combine(_workspacePath, "materials-locked.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "locked-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-locked-import"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        using var lockStream = new FileStream(csvPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var response = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest { FilePath = csvPath });

        Assert.False(response.Success);
        Assert.Equal(csvPath, response.FilePath);
        Assert.Empty(response.Parts);
        var error = Assert.Single(response.Errors);
        Assert.Equal("file-in-use", error.Code);
        Assert.Contains("Close the file and try importing again.", response.Message);
    }

    [Fact]
    public async Task Import_file_can_create_a_new_material_and_map_import_rows_to_it()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "create-material-on-import.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-create-on-import.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "created-on-import");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-create-material"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        await File.WriteAllTextAsync(
            csvPath,
            """
            Id,Length,Width,Quantity,Material
            P-001,20,10,1,Import MDF
            """);

        var response = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest
            {
                FilePath = csvPath,
                NewMaterials =
                [
                    new ImportNewMaterialRequest
                    {
                        SourceMaterialName = "Import MDF",
                        Material = new Material
                        {
                            Name = "Import MDF 3/4",
                            SheetLength = 96m,
                            SheetWidth = 48m,
                            AllowRotation = true,
                            DefaultSpacing = 0.125m,
                            DefaultEdgeMargin = 0.5m
                        }
                    }
                ]
            });

        Assert.True(response.Success);
        var row = Assert.Single(response.Parts);
        Assert.Equal("Import MDF 3/4", row.MaterialName);
        var resolution = Assert.Single(response.MaterialResolutions);
        Assert.Equal("Import MDF", resolution.SourceMaterialName);
        Assert.Equal(ImportMaterialResolutionStatuses.Created, resolution.Status);

        var materials = await repository.GetAllAsync();
        Assert.Contains(materials, material => material.MaterialId == "created-on-import" && material.Name == "Import MDF 3/4");
    }

    [Fact]
    public async Task Import_file_and_part_row_edit_messages_round_trip_optional_group_assignments()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "grouped-import.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-grouped-import.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "grouped-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-grouped-import"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var createMaterialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Grouped Material",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createMaterialResult.Success);
        var material = Assert.IsType<Material>(createMaterialResult.Material);

        await File.WriteAllTextAsync(
            csvPath,
            $$"""
            Id,Length,Width,Quantity,Material,Group
            P-001,20,10,1,{{material.Name}},Casework
            """);

        var imported = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest { FilePath = csvPath });

        Assert.True(imported.Success);
        var importedRow = Assert.Single(imported.Parts);
        Assert.Equal("Casework", importedRow.Group);
        Assert.Contains(
            imported.ColumnMappings,
            mapping => mapping.TargetField == ImportFieldNames.Group && mapping.SourceColumn == ImportFieldNames.Group);

        var updated = await DispatchAsync<ImportResponse>(
            dispatcher,
            BridgeMessageTypes.UpdatePartRow,
            new UpdatePartRowRequest
            {
                Parts = imported.Parts,
                Part = new PartRowUpdate
                {
                    RowId = importedRow.RowId,
                    ImportedId = importedRow.ImportedId,
                    Length = importedRow.LengthText ?? importedRow.Length.ToString(),
                    Width = importedRow.WidthText ?? importedRow.Width.ToString(),
                    Quantity = importedRow.QuantityText ?? importedRow.Quantity.ToString(),
                    MaterialName = importedRow.MaterialName,
                    Group = "Doors"
                }
            });

        Assert.True(updated.Success);
        Assert.Equal("Doors", Assert.Single(updated.Parts).Group);

        var added = await DispatchAsync<ImportResponse>(
            dispatcher,
            BridgeMessageTypes.AddPartRow,
            new AddPartRowRequest
            {
                Parts = updated.Parts,
                Part = new PartRowUpdate
                {
                    ImportedId = "P-002",
                    Length = "18",
                    Width = "12",
                    Quantity = "1",
                    MaterialName = material.Name,
                    Group = "   "
                }
            });

        Assert.True(added.Success);
        Assert.Null(added.Parts[1].Group);
    }

    [Fact]
    public async Task Import_file_response_keeps_group_alias_columns_visible_for_manual_mapping_review()
    {
        Directory.CreateDirectory(_workspacePath);

        var csvPath = Path.Combine(_workspacePath, "group-alias-review.csv");
        var materialFilePath = Path.Combine(_workspacePath, "materials-group-alias-review.json");
        var repository = new JsonMaterialRepository(materialFilePath);
        var materialService = new MaterialService(repository, idGenerator: () => "group-alias-material");
        var validator = new PartRowValidator();
        var dispatcher = DesktopBridgeRegistration.CreateDefault(
            new RecordingFileDialogService(),
            materialService,
            new ProjectService(materialService, idGenerator: () => "project-group-alias-review"),
            new FileImportDispatcher(
                new CsvImportService(repository, validator),
                new XlsxImportService(repository, validator)),
            new PartEditorService(repository, validator),
            new ShelfNestingService(),
            () => new WebUiContentLocation("F:\\mock-ui", "Mock UI build", true));

        var createMaterialResult = await materialService.CreateAsync(
            new Material
            {
                Name = "Alias Review Material",
                SheetLength = 96m,
                SheetWidth = 48m,
                AllowRotation = true,
                DefaultSpacing = 0.125m,
                DefaultEdgeMargin = 0.5m
            });

        Assert.True(createMaterialResult.Success);
        var material = Assert.IsType<Material>(createMaterialResult.Material);

        await File.WriteAllTextAsync(
            csvPath,
            $$"""
            Id,Length,Width,Quantity,Material,Panel Group
            P-001,20,10,1,{{material.Name}},Casework
            """);

        var response = await DispatchAsync<ImportFileResponse>(
            dispatcher,
            BridgeMessageTypes.ImportFile,
            new ImportFileRequest { FilePath = csvPath });

        Assert.True(response.Success);
        Assert.Contains("Panel Group", response.AvailableColumns);
        Assert.Contains(
            response.ColumnMappings,
            mapping => mapping.TargetField == ImportFieldNames.Group && mapping.SourceColumn is null);
        Assert.Null(Assert.Single(response.Parts).Group);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }

    private static void WriteWorkbook(string filePath, string materialName)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Parts");
        string[] headers = ["Id", "Length", "Width", "Quantity", "Material"];

        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        sheet.Cell(2, 1).Value = "P-001";
        sheet.Cell(2, 2).Value = 20;
        sheet.Cell(2, 3).Value = 10;
        sheet.Cell(2, 4).Value = 1;
        sheet.Cell(2, 5).Value = materialName;

        workbook.SaveAs(filePath);
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

    private sealed class RecordingFileDialogService(IEnumerable<string>? openPaths = null) : IFileDialogService
    {
        private readonly Queue<string> _openPaths = new(openPaths ?? []);

        public List<OpenFileDialogRequest> OpenRequests { get; } = [];

        public Task<OpenFileDialogResponse> OpenAsync(
            OpenFileDialogRequest request,
            CancellationToken cancellationToken = default)
        {
            OpenRequests.Add(request);
            return Task.FromResult(
                _openPaths.Count == 0
                    ? OpenFileDialogResponse.Cancelled()
                    : new OpenFileDialogResponse(true, _openPaths.Dequeue(), null, "File selected."));
        }

        public Task<SaveFileDialogResponse> SaveAsync(
            SaveFileDialogRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SaveFileDialogResponse.Cancelled());
    }

    private sealed class BlockingImportService : IImportService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ImportResponse> ImportAsync(
            ImportRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new ImportResponse { Success = true };
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult(true);
                throw;
            }
        }

        public Task<ImportResponse> ImportAsync(
            TextReader reader,
            ImportOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingMaterialService : IMaterialService
    {
        public TaskCompletionSource CreateStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<Material>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Material>>(Array.Empty<Material>());

        public Task<MaterialOperationResult> GetAsync(string materialId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterialOperationResult());

        public async Task<MaterialOperationResult> CreateAsync(
            Material material,
            CancellationToken cancellationToken = default)
        {
            CreateStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new MaterialOperationResult { Success = true, Material = material };
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult(true);
                throw;
            }
        }

        public Task<MaterialOperationResult> UpdateAsync(Material material, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterialOperationResult());

        public Task<MaterialDeleteResult> DeleteAsync(
            string materialId,
            bool isInUse = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterialDeleteResult { Success = true });
    }
}
