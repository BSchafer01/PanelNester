using System.Diagnostics;
using System.IO;
using PanelNester.Desktop;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;
using PanelNester.Services.Nesting;
using PanelNester.Services.Projects;
using PanelNester.Services.Reporting;

namespace PanelNester.Desktop.Bridge;

public static class DesktopBridgeRegistration
{
    public static BridgeMessageDispatcher CreateDefault(
        IFileDialogService fileDialogService,
        IMaterialService materialService,
        IProjectService projectService,
        IImportService importService,
        INestingService nestingService,
        Func<WebUiContentLocation> contentLocationAccessor,
        DesktopAppSettingsStore? desktopAppSettingsStore = null,
        IMaterialLibraryLocationService? materialLibraryLocationService = null,
        Action<string>? exportedPdfOpener = null,
        IExcelReportExporter? excelReportExporter = null) =>
        CreateDefault(
            fileDialogService,
            materialService,
            projectService,
            importService,
            new NoOpPartEditorService(),
            nestingService,
            contentLocationAccessor,
            desktopAppSettingsStore,
            materialLibraryLocationService,
            exportedPdfOpener,
            excelReportExporter);

    public static BridgeMessageDispatcher CreateDefault(
        IFileDialogService fileDialogService,
        IMaterialService materialService,
        IProjectService projectService,
        IImportService importService,
        IPartEditorService partEditorService,
        INestingService nestingService,
        IBatchNestingService? batchNestingService,
        IReportDataService? reportDataService,
        IPdfReportExporter? pdfReportExporter,
        IExcelReportExporter? excelReportExporter,
        Func<WebUiContentLocation> contentLocationAccessor,
        DesktopAppSettingsStore? desktopAppSettingsStore = null,
        IMaterialLibraryLocationService? materialLibraryLocationService = null,
        Action<string>? exportedPdfOpener = null) =>
        CreateDefault(
            fileDialogService,
            materialService,
            projectService,
            importService,
            partEditorService,
            nestingService,
            batchNestingService,
            reportDataService,
            pdfReportExporter,
            excelReportExporter,
            stiffenerTakeoffService: null,
            stiffenerPdfReportExporter: null,
            contentLocationAccessor,
            desktopAppSettingsStore,
            materialLibraryLocationService,
            exportedPdfOpener);

    public static BridgeMessageDispatcher CreateDefault(
        IFileDialogService fileDialogService,
        IMaterialService materialService,
        IProjectService projectService,
        IImportService importService,
        IPartEditorService partEditorService,
        INestingService nestingService,
        IBatchNestingService? batchNestingService,
        IReportDataService? reportDataService,
        IPdfReportExporter? pdfReportExporter,
        Func<WebUiContentLocation> contentLocationAccessor,
        DesktopAppSettingsStore? desktopAppSettingsStore = null,
        IMaterialLibraryLocationService? materialLibraryLocationService = null,
        Action<string>? exportedPdfOpener = null) =>
        CreateDefault(
            fileDialogService,
            materialService,
            projectService,
            importService,
            partEditorService,
            nestingService,
            batchNestingService,
            reportDataService,
            pdfReportExporter,
            excelReportExporter: null,
            contentLocationAccessor,
            desktopAppSettingsStore,
            materialLibraryLocationService,
            exportedPdfOpener);

    public static BridgeMessageDispatcher CreateDefault(
        IFileDialogService fileDialogService,
        IMaterialService materialService,
        IProjectService projectService,
        IImportService importService,
        IPartEditorService partEditorService,
        INestingService nestingService,
        Func<WebUiContentLocation> contentLocationAccessor,
        DesktopAppSettingsStore? desktopAppSettingsStore = null,
        IMaterialLibraryLocationService? materialLibraryLocationService = null,
        Action<string>? exportedPdfOpener = null,
        IExcelReportExporter? excelReportExporter = null) =>
        CreateDefault(
            fileDialogService,
            materialService,
            projectService,
            importService,
            partEditorService,
            nestingService,
            batchNestingService: null,
            reportDataService: null,
            pdfReportExporter: null,
            excelReportExporter,
            stiffenerTakeoffService: null,
            stiffenerPdfReportExporter: null,
            contentLocationAccessor,
            desktopAppSettingsStore,
            materialLibraryLocationService,
            exportedPdfOpener);

    public static BridgeMessageDispatcher CreateDefault(
        IFileDialogService fileDialogService,
        IMaterialService materialService,
        IProjectService projectService,
        IImportService importService,
        IPartEditorService partEditorService,
        INestingService nestingService,
        IBatchNestingService? batchNestingService,
        IReportDataService? reportDataService,
        IPdfReportExporter? pdfReportExporter,
        IExcelReportExporter? excelReportExporter,
        IStiffenerTakeoffService? stiffenerTakeoffService,
        IStiffenerPdfReportExporter? stiffenerPdfReportExporter,
        Func<WebUiContentLocation> contentLocationAccessor,
        DesktopAppSettingsStore? desktopAppSettingsStore = null,
        IMaterialLibraryLocationService? materialLibraryLocationService = null,
        Action<string>? exportedPdfOpener = null)
    {
        ArgumentNullException.ThrowIfNull(fileDialogService);
        ArgumentNullException.ThrowIfNull(materialService);
        ArgumentNullException.ThrowIfNull(projectService);
        ArgumentNullException.ThrowIfNull(importService);
        ArgumentNullException.ThrowIfNull(partEditorService);
        ArgumentNullException.ThrowIfNull(nestingService);
        ArgumentNullException.ThrowIfNull(contentLocationAccessor);

        var dispatcher = new BridgeMessageDispatcher();
        var importSessions = new ImportSessionCoordinator(importService);
        var cutPlanGeneration = new CutPlanGenerationCoordinator();

        dispatcher.Register<BridgeHandshakeRequest>(
            BridgeMessageTypes.BridgeHandshake,
            (request, _) =>
            {
                var contentLocation = contentLocationAccessor();
                var response = new BridgeHandshakeResponse(
                    true,
                    "OptiFab Desktop Host",
                    GetHostVersion(),
                    "webview2",
                    GetCapabilities(request, dispatcher),
                    $"Connected to {contentLocation.DisplayName}.");

                return Task.FromResult<object?>(response);
            });

        dispatcher.Register<BridgeUiReadyRequest>(
            BridgeMessageTypes.BridgeUiReady,
            (_, _) => Task.FromResult<object?>(new BridgeOperationResponse(true, "Web UI ready.")));

        dispatcher.Register<OpenFileDialogRequest>(
            BridgeMessageTypes.OpenFileDialog,
            async (request, cancellationToken) =>
                await fileDialogService.OpenAsync(request, cancellationToken).ConfigureAwait(false));

        dispatcher.Register<ImportFileRequest>(
            BridgeMessageTypes.ImportFile,
            async (request, cancellationToken) =>
            {
                try
                {
                var filePath = NormalizeFilePath(request.FilePath);
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    var dialogResult = await fileDialogService
                        .OpenAsync(
                            new OpenFileDialogRequest("Import OptiFab parts", ImportFileFilters),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (!dialogResult.Success || string.IsNullOrWhiteSpace(dialogResult.FilePath))
                    {
                        return ImportFileResponse.Cancelled();
                    }

                    filePath = dialogResult.FilePath;
                }

                if (IsExcelWorkbookPath(filePath))
                {
                    return ImportFileResponse.Failure(
                        filePath,
                        "workbook-discovery-required",
                        "Excel Workbook imports must begin with Workbook discovery.");
                }

                var importPreparation = await PrepareImportOptionsAsync(request, materialService, cancellationToken)
                    .ConfigureAwait(false);
                if (!importPreparation.Success)
                {
                    var failedResponse = new ImportResponse
                    {
                        Success = false,
                        Errors = importPreparation.Errors
                    };

                    return ImportFileResponse.FromImportResponse(
                        failedResponse,
                        filePath,
                        GetFirstErrorMessage(failedResponse.Errors, "Import material preparation failed."));
                }

                var result = await importService
                    .ImportAsync(
                        new ImportRequest
                        {
                            FilePath = filePath,
                            Options = importPreparation.Options
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                result = MarkCreatedMaterialResolutions(result, importPreparation.CreatedSourceMaterials);

                return ImportFileResponse.FromImportResponse(
                    result,
                    filePath,
                    BuildImportFileMessage(result, filePath));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return ImportFileResponse.Failure(
                        request.FilePath,
                        "import-host-error",
                        ex.Message,
                        "The desktop host could not complete the file import. Check the material library and try again.");
                }
            });

        dispatcher.Register<BeginImportSessionRequest>(
            BridgeMessageTypes.BeginImportSession,
            async (request, cancellationToken) =>
            {
                var importSourcePath = NormalizeFilePath(request.ImportSourcePath);
                var importSourceIdentityPath = importSourcePath;
                string? droppedSnapshotPath = null;
                byte[]? droppedContents = null;
                if (!string.IsNullOrWhiteSpace(request.ImportSourceContentBase64))
                {
                    var fileName = Path.GetFileName(request.ImportSourceFileName?.Trim());
                    var extension = Path.GetExtension(fileName);
                    if (string.IsNullOrWhiteSpace(fileName) ||
                        !(string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase)))
                    {
                        return ImportSessionResponse.Failure(
                            request.SessionId,
                            fileName,
                            ImportSessionPhase.Opening,
                            "unsupported-import-source",
                            "Dropped Import Sources must be CSV, XLSX, or XLSM files.");
                    }

                    try
                    {
                        droppedContents = Convert.FromBase64String(request.ImportSourceContentBase64);
                    }
                    catch (FormatException)
                    {
                        return ImportSessionResponse.Failure(
                            request.SessionId,
                            fileName,
                            ImportSessionPhase.Opening,
                            "invalid-import-source-content",
                            "The dropped Import Source content could not be decoded.");
                    }

                    if (droppedContents.LongLength > WorkbookSafetyLimits.DesktopDefault.MaximumCompressedBytes)
                    {
                        Array.Clear(droppedContents);
                        return ImportSessionResponse.Failure(
                            request.SessionId,
                            fileName,
                            ImportSessionPhase.Opening,
                            "workbook-safety-ceiling-exceeded",
                            "The dropped Import Source is above the desktop safety ceiling.");
                    }

                    var droppedSnapshotDirectory = Path.Combine(Path.GetTempPath(), "PanelNester.DroppedImports");
                    Directory.CreateDirectory(droppedSnapshotDirectory);
                    droppedSnapshotPath = Path.Combine(droppedSnapshotDirectory, $"{Guid.NewGuid():N}{extension}");
                    await File.WriteAllBytesAsync(droppedSnapshotPath, droppedContents, cancellationToken)
                        .ConfigureAwait(false);
                    importSourcePath = droppedSnapshotPath;
                    importSourceIdentityPath = fileName;
                }
                else if (string.IsNullOrWhiteSpace(importSourcePath))
                {
                    var dialogResult = await fileDialogService
                        .OpenAsync(
                            new OpenFileDialogRequest("Import OptiFab parts", ImportFileFilters),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!dialogResult.Success || string.IsNullOrWhiteSpace(dialogResult.FilePath))
                    {
                        return ImportSessionResponse.Failure(
                            request.SessionId,
                            null,
                            ImportSessionPhase.Opening,
                            "cancelled",
                            "File selection was cancelled.");
                    }

                    importSourcePath = dialogResult.FilePath;
                    importSourceIdentityPath = importSourcePath;
                }

                try
                {
                    var result = await importSessions
                        .BeginAsync(
                            request.SessionId,
                            importSourcePath,
                            request.ProjectKind,
                            cancellationToken,
                            importSourceIdentityPath)
                        .ConfigureAwait(false);
                    return BuildImportSessionResponse(request.SessionId, result, ImportSessionPhase.Reading);
                }
                catch (ImportSessionException ex)
                {
                    return ImportSessionResponse.Failure(request.SessionId, importSourcePath, ImportSessionPhase.Opening, ex.Code, ex.Message);
                }
                catch (OperationCanceledException)
                {
                    return ImportSessionResponse.Failure(
                        request.SessionId,
                        importSourcePath,
                        ImportSessionPhase.Cancelled,
                        "cancelled",
                        "Import Session was cancelled.");
                }
                catch (Exception ex)
                {
                    return ImportSessionResponse.Failure(
                        request.SessionId,
                        importSourcePath,
                        ImportSessionPhase.Failed,
                        "import-host-error",
                        ex.Message,
                        "The desktop host could not begin the Import Session.");
                }
                finally
                {
                    if (droppedContents is not null)
                    {
                        Array.Clear(droppedContents);
                    }

                    if (!string.IsNullOrWhiteSpace(droppedSnapshotPath) && File.Exists(droppedSnapshotPath))
                    {
                        File.Delete(droppedSnapshotPath);
                    }
                }
            });

        dispatcher.Register<PreviewImportSessionRequest>(
            BridgeMessageTypes.PreviewImportSession,
            async (request, cancellationToken) =>
            {
                try
                {
                    var result = await importSessions
                        .PreviewAsync(
                            request.SessionId,
                            request.Options,
                            request.NewMaterials,
                            request.WorksheetName,
                            request.HeadingRange,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return BuildImportSessionResponse(request.SessionId, result, ImportSessionPhase.Validating);
                }
                catch (ImportSessionException ex)
                {
                    return ImportSessionResponse.Failure(request.SessionId, null, ImportSessionPhase.Validating, ex.Code, ex.Message);
                }
                catch (OperationCanceledException)
                {
                    return ImportSessionResponse.Failure(
                        request.SessionId,
                        null,
                        ImportSessionPhase.Cancelled,
                        "cancelled",
                        "Import Session was cancelled.");
                }
                catch (Exception ex)
                {
                    return ImportSessionResponse.Failure(
                        request.SessionId,
                        null,
                        ImportSessionPhase.Failed,
                        "import-host-error",
                        ex.Message,
                        "The desktop host could not preview the Import Session.");
                }
            });

        dispatcher.Register<FinalizeImportSessionRequest>(
            BridgeMessageTypes.FinalizeImportSession,
            async (request, cancellationToken) =>
            {
                var createdMaterials = new List<Material>();
                if (request.Project is null)
                {
                    importSessions.Cancel(request.SessionId);
                    return ImportSessionResponse.Failure(
                        request.SessionId,
                        null,
                        ImportSessionPhase.Finalizing,
                        "import-session-project-required",
                        "Import Session finalization requires the current project.");
                }

                try
                {
                    using var finalization = importSessions.BeginFinalization(request.SessionId, cancellationToken);
                    if (finalization.IsWorkbook && request.Worksheets.Count == 0)
                    {
                        return ImportSessionResponse.Failure(
                            request.SessionId,
                            null,
                            ImportSessionPhase.Failed,
                            "import-worksheet-selection-required",
                            "Select at least one Worksheet before finalizing the Import Session.");
                    }

                    if (request.Worksheets.Count > 0)
                    {
                        var worksheetImports = new List<FinalizedWorksheetImport>();
                        ImportSourceMetadata? workbookImportSource = null;
                        var orderedSelections = request.Worksheets
                            .OrderBy(selection => selection.OriginalPosition)
                            .ToArray();
                        var conflictingMaterialLabel = FindConflictingMaterialResolution(
                            orderedSelections,
                            request.NewMaterials);
                        if (conflictingMaterialLabel is not null)
                        {
                            return ImportSessionResponse.Failure(
                                request.SessionId,
                                null,
                                ImportSessionPhase.Failed,
                                "import-material-resolution-conflict",
                                $"Material label '{conflictingMaterialLabel}' has conflicting resolutions across the Workbook.");
                        }
                        foreach (var selection in orderedSelections)
                        {
                            finalization.CancellationToken.ThrowIfCancellationRequested();
                            if (request.Project.ProjectKind != ProjectKind.StockLength)
                            {
                                finalization.EnsureWorksheetReady(selection, request.NewMaterials);
                            }
                        }

                        var workbookPreparation = await PrepareImportOptionsAsync(
                                new ImportFileRequest
                                {
                                    Options = new ImportOptions
                                    {
                                        MaterialMappings = BuildWorkbookMaterialMappings(orderedSelections)
                                    },
                                    NewMaterials = request.NewMaterials
                                },
                                materialService,
                                finalization.CancellationToken)
                            .ConfigureAwait(false);
                        createdMaterials.AddRange(workbookPreparation.CreatedMaterials);
                        if (!workbookPreparation.Success)
                        {
                            var preparationMessage = GetFirstErrorMessage(
                                workbookPreparation.Errors,
                                "Import material preparation failed.");
                            return ImportSessionResponse.Failure(
                                request.SessionId,
                                null,
                                ImportSessionPhase.Failed,
                                GetFirstErrorCode(
                                    workbookPreparation.Errors,
                                    "import-material-preparation-failed"),
                                preparationMessage);
                        }

                        for (var selectionIndex = 0; selectionIndex < orderedSelections.Length; selectionIndex++)
                        {
                            var selection = orderedSelections[selectionIndex];
                            finalization.CancellationToken.ThrowIfCancellationRequested();
                            finalization.ReportProgress(
                                WorkbookImportPhase.ReadingWorksheet,
                                $"Reading Worksheet {selectionIndex + 1} of {orderedSelections.Length}",
                                selectionIndex + 1,
                                orderedSelections.Length,
                                selection.WorksheetName);
                            var worksheetOptions = (selection.Options ?? new ImportOptions()) with
                            {
                                ProjectKind = request.Project.ProjectKind,
                                MaterialMappings = workbookPreparation.Options.MaterialMappings
                            };

                            var worksheetResult = await finalization
                                .ImportAsync(
                                    worksheetOptions,
                                    selection.WorksheetName,
                                    selection.HeadingRange)
                                .ConfigureAwait(false);
                            var importedWorksheet = worksheetResult.Response.Worksheet;
                            var normalizedSelection = importedWorksheet is null
                                ? selection
                                : selection with
                                {
                                    WorksheetName = importedWorksheet.WorksheetName,
                                    OriginalPosition = importedWorksheet.OriginalPosition
                                };
                            var preparedResponse = MarkCreatedMaterialResolutions(
                                worksheetResult.Response,
                                workbookPreparation.CreatedSourceMaterials);
                            var importedResponse = preparedResponse;
                            preparedResponse = await ProjectImportFinalizer.ApplyPartOverridesAsync(
                                    preparedResponse,
                                    normalizedSelection.PartOverrides,
                                    partEditorService,
                                    finalization.CancellationToken)
                                .ConfigureAwait(false);
                            normalizedSelection = ProjectImportFinalizer.ReconcilePartOverrides(
                                normalizedSelection,
                                importedResponse,
                                preparedResponse);
                            preparedResponse = ProjectImportFinalizer.ResolveSourceRows(
                                preparedResponse,
                                normalizedSelection);
                            worksheetResult = worksheetResult with
                            {
                                Response = PrefixWorksheetRowIds(
                                    preparedResponse,
                                    normalizedSelection.OriginalPosition)
                            };
                            workbookImportSource = worksheetResult.ImportSource;
                            if (!worksheetResult.Response.Success)
                            {
                                await RollbackCreatedMaterialsAsync(materialService, createdMaterials)
                                    .ConfigureAwait(false);
                                return BuildImportSessionResponse(
                                    request.SessionId,
                                    worksheetResult,
                                    ImportSessionPhase.Failed);
                            }

                            worksheetImports.Add(new FinalizedWorksheetImport(
                                normalizedSelection,
                                worksheetOptions,
                                worksheetResult.Response));
                        }

                        var workbookProject = ProjectImportFinalizer.FinalizeWorkbook(
                            request.Project,
                            workbookImportSource!,
                            worksheetImports,
                            request.ReplaceExistingImportSource,
                            (phase, label) => finalization.ReportProgress(phase, label),
                            finalization.CancellationToken,
                            request.StockLengthGrouping);
                        var previewSummary = BuildWorkbookPreviewSummary(
                            worksheetImports,
                            workbookProject.State.OptimizationGroups);
                        var combinedResult = CombineWorksheetImports(
                            workbookImportSource!,
                            worksheetImports,
                            workbookProject.State.Parts,
                            workbookProject.State.OptimizationGroups
                                .SelectMany(group => group.RequiredPieces)
                                .Where(piece => !piece.IsManual)
                                .ToArray());
                        var resultCounts = BuildStockLengthImportResultCounts(
                            request.Project,
                            worksheetImports,
                            workbookProject);
                        finalization.CancellationToken.ThrowIfCancellationRequested();
                        var finalProgress = finalization.GetProgress();
                        combinedResult = combinedResult with
                        {
                            Progress = finalProgress.Progress,
                            ProgressHistory = finalProgress.History
                        };
                        return BuildImportSessionResponse(
                            request.SessionId,
                            combinedResult,
                            ImportSessionPhase.Finalized,
                            workbookProject,
                            finalized: true,
                            previewSummary: previewSummary,
                            resultCounts: resultCounts);
                    }

                    var importPreparation = await PrepareImportOptionsAsync(
                            new ImportFileRequest
                            {
                                Options = request.Options,
                                NewMaterials = request.NewMaterials
                            },
                            materialService,
                            finalization.CancellationToken)
                        .ConfigureAwait(false);
                    createdMaterials.AddRange(importPreparation.CreatedMaterials);
                    if (!importPreparation.Success)
                    {
                        importSessions.Cancel(request.SessionId);
                        var message = GetFirstErrorMessage(
                            importPreparation.Errors,
                            "Import material preparation failed.");
                        return ImportSessionResponse.Failure(
                            request.SessionId,
                            null,
                            ImportSessionPhase.Failed,
                            GetFirstErrorCode(importPreparation.Errors, "import-material-preparation-failed"),
                            message);
                    }

                    var result = await finalization.ImportAsync(importPreparation.Options).ConfigureAwait(false);
                    result = result with
                    {
                        Response = MarkCreatedMaterialResolutions(
                            result.Response,
                            importPreparation.CreatedSourceMaterials)
                    };
                    if (!result.Response.Success)
                    {
                        await RollbackCreatedMaterialsAsync(materialService, importPreparation.CreatedMaterials)
                            .ConfigureAwait(false);
                        return BuildImportSessionResponse(request.SessionId, result, ImportSessionPhase.Failed);
                    }

                    var project = ProjectImportFinalizer.Finalize(
                        request.Project,
                        result.ImportSource,
                        importPreparation.Options,
                        result.Response,
                        request.TargetOptimizationGroupId,
                        request.ReplaceExistingImportSource,
                        (phase, label) => finalization.ReportProgress(phase, label),
                        finalization.CancellationToken);
                    finalization.CancellationToken.ThrowIfCancellationRequested();
                    var completedProgress = finalization.GetProgress();
                    result = result with
                    {
                        Progress = completedProgress.Progress,
                        ProgressHistory = completedProgress.History
                    };
                    return BuildImportSessionResponse(
                        request.SessionId,
                        result,
                        ImportSessionPhase.Finalized,
                        project,
                        finalized: true);
                }
                catch (ImportSessionException ex)
                {
                    await RollbackCreatedMaterialsAsync(materialService, createdMaterials).ConfigureAwait(false);
                    return ImportSessionResponse.Failure(request.SessionId, null, ImportSessionPhase.Finalizing, ex.Code, ex.Message);
                }
                catch (OperationCanceledException)
                {
                    await RollbackCreatedMaterialsAsync(materialService, createdMaterials).ConfigureAwait(false);
                    return ImportSessionResponse.Failure(
                        request.SessionId,
                        null,
                        ImportSessionPhase.Cancelled,
                        "cancelled",
                        "Import Session was cancelled.");
                }
                catch (Exception ex)
                {
                    await RollbackCreatedMaterialsAsync(materialService, createdMaterials).ConfigureAwait(false);
                    importSessions.Cancel(request.SessionId);
                    return ImportSessionResponse.Failure(
                        request.SessionId,
                        null,
                        ImportSessionPhase.Failed,
                        "import-host-error",
                        ex.Message,
                        "The desktop host could not finalize the Import Session.");
                }
            });

        dispatcher.Register<CancelImportSessionRequest>(
            BridgeMessageTypes.CancelImportSession,
            (request, _) =>
            {
                try
                {
                    var released = importSessions.Cancel(request.SessionId);
                    return Task.FromResult<object?>(new CancelImportSessionResponse(
                        true,
                        request.SessionId,
                        released,
                        null,
                        released
                            ? "Import Session cancelled and snapshot released."
                            : "No active Import Session was found."));
                }
                catch (ImportSessionException ex)
                {
                    return Task.FromResult<object?>(new CancelImportSessionResponse(
                        false,
                        request.SessionId,
                        false,
                        BridgeError.Create(ex.Code, ex.Message),
                        ex.Message));
                }
            });

        dispatcher.Register<GetImportSessionProgressRequest>(
            BridgeMessageTypes.GetImportSessionProgress,
            (request, _) =>
            {
                try
                {
                    var progress = importSessions.GetProgress(request.SessionId);
                    return Task.FromResult<object?>(new GetImportSessionProgressResponse(
                        true,
                        request.SessionId,
                        progress.Progress,
                        progress.History,
                        null,
                        null));
                }
                catch (ImportSessionException ex)
                {
                    return Task.FromResult<object?>(new GetImportSessionProgressResponse(
                        false,
                        request.SessionId,
                        null,
                        Array.Empty<WorkbookImportProgress>(),
                        BridgeError.Create(ex.Code, ex.Message),
                        ex.Message));
                }
            });

        dispatcher.Register<NewProjectRequest>(
            BridgeMessageTypes.NewProject,
            async (request, cancellationToken) =>
            {
                var result = await projectService
                    .NewAsync(request.Metadata, request.Settings, request.ProjectKind, cancellationToken)
                    .ConfigureAwait(false);

                return result.Success && result.Project is not null
                    ? new NewProjectResponse(
                        true,
                        result.Project,
                        null,
                        $"Created project '{result.Project.Metadata.ProjectName}'.")
                    : NewProjectResponse.Failure(
                        GetFirstErrorCode(result.Errors, "project-create-failed"),
                        GetFirstErrorMessage(result.Errors, "Project could not be created."));
            });

        dispatcher.Register<OpenProjectRequest>(
            BridgeMessageTypes.OpenProject,
            async (request, cancellationToken) =>
            {
                var filePath = NormalizeFilePath(request.FilePath);
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    var dialogResult = await fileDialogService
                        .OpenAsync(
                            new OpenFileDialogRequest("Open an OptiFab project", ProjectFileFilters),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (!dialogResult.Success || string.IsNullOrWhiteSpace(dialogResult.FilePath))
                    {
                        return OpenProjectResponse.Cancelled();
                    }

                    filePath = dialogResult.FilePath;
                }

                var result = await projectService.LoadAsync(filePath, cancellationToken).ConfigureAwait(false);
                return result.Success && result.Project is not null
                    ? new OpenProjectResponse(
                        true,
                        result.Project,
                        result.FilePath ?? filePath,
                        null,
                        $"Opened project '{result.Project.Metadata.ProjectName}'.")
                    : OpenProjectResponse.Failure(
                        result.FilePath ?? filePath,
                        GetFirstErrorCode(result.Errors, "project-not-found"),
                        GetFirstErrorMessage(result.Errors, "Project could not be opened."));
            });

        dispatcher.Register<SaveProjectRequest>(
            BridgeMessageTypes.SaveProject,
            async (request, cancellationToken) =>
            {
                var filePath = request.FilePath;
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    var dialogResult = await fileDialogService
                        .SaveAsync(
                            new SaveFileDialogRequest("Save OptiFab project", BuildProjectFileName(request.Project), ProjectFileFilters),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (!dialogResult.Success || string.IsNullOrWhiteSpace(dialogResult.FilePath))
                    {
                        return SaveProjectResponse.Cancelled();
                    }

                    filePath = dialogResult.FilePath;
                }

                var result = await projectService.SaveAsync(request.Project, filePath, cancellationToken).ConfigureAwait(false);
                return result.Success && result.Project is not null
                    ? new SaveProjectResponse(
                        true,
                        result.Project,
                        result.FilePath ?? filePath,
                        null,
                        $"Saved project '{result.Project.Metadata.ProjectName}'.")
                    : SaveProjectResponse.Failure(
                        result.FilePath ?? filePath,
                        GetFirstErrorCode(result.Errors, "project-save-failed"),
                        GetFirstErrorMessage(result.Errors, "Project could not be saved."));
            });

        dispatcher.Register<SaveProjectAsRequest>(
            BridgeMessageTypes.SaveProjectAs,
            async (request, cancellationToken) =>
            {
                var filePath = request.FilePath;
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    var dialogResult = await fileDialogService
                        .SaveAsync(
                            new SaveFileDialogRequest(
                                "Save OptiFab project as",
                                string.IsNullOrWhiteSpace(request.SuggestedFileName)
                                    ? BuildProjectFileName(request.Project)
                                    : request.SuggestedFileName,
                                ProjectFileFilters),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (!dialogResult.Success || string.IsNullOrWhiteSpace(dialogResult.FilePath))
                    {
                        return SaveProjectAsResponse.Cancelled();
                    }

                    filePath = dialogResult.FilePath;
                }

                var result = await projectService.SaveAsync(request.Project, filePath, cancellationToken).ConfigureAwait(false);
                return result.Success && result.Project is not null
                    ? new SaveProjectAsResponse(
                        true,
                        result.Project,
                        result.FilePath ?? filePath,
                        null,
                        $"Saved project '{result.Project.Metadata.ProjectName}'.")
                    : SaveProjectAsResponse.Failure(
                        result.FilePath ?? filePath,
                        GetFirstErrorCode(result.Errors, "project-save-failed"),
                        GetFirstErrorMessage(result.Errors, "Project could not be saved."));
            });

        dispatcher.Register<GetProjectMetadataRequest>(
            BridgeMessageTypes.GetProjectMetadata,
            (request, _) =>
            {
                var metadata = request.Project.Metadata;
                var settings = request.Project.Settings;

                return Task.FromResult<object?>(
                    new GetProjectMetadataResponse(
                        true,
                        metadata,
                        settings,
                        null,
                        $"Loaded metadata for '{metadata.ProjectName}'."));
            });

        if (desktopAppSettingsStore is not null)
        {
            dispatcher.Register<GetDesktopAppSettingsRequest>(
                BridgeMessageTypes.GetDesktopAppSettings,
                (_, _) =>
                {
                    var settings = desktopAppSettingsStore.Load();
                    return Task.FromResult<object?>(
                        new GetDesktopAppSettingsResponse(
                            true,
                            ToPayload(settings),
                            null,
                            "Loaded desktop application settings."));
                });

            dispatcher.Register<UpdateDesktopAppSettingsRequest>(
                BridgeMessageTypes.UpdateDesktopAppSettings,
                (request, _) =>
                {
                    try
                    {
                        var currentSettings = desktopAppSettingsStore.Load();
                        var nextSettings = NormalizeDesktopAppSettings(request.Settings, currentSettings);
                        desktopAppSettingsStore.Save(nextSettings);

                        return Task.FromResult<object?>(
                            new UpdateDesktopAppSettingsResponse(
                                true,
                                ToPayload(nextSettings),
                                null,
                                "Updated desktop application settings."));
                    }
                    catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
                    {
                        return Task.FromResult<object?>(
                            UpdateDesktopAppSettingsResponse.Failure(
                                "desktop-settings-update-failed",
                                ex.Message));
                    }
                });
        }

        if (stiffenerTakeoffService is not null)
        {
            dispatcher.Register<GetStiffenerTakeoffRequest>(
                BridgeMessageTypes.GetStiffenerTakeoff,
                async (request, cancellationToken) =>
                {
                    try
                    {
                        var report = await stiffenerTakeoffService
                            .BuildAsync(
                                new StiffenerTakeoffRequest
                                {
                                    Project = request.Project
                                },
                                cancellationToken)
                            .ConfigureAwait(false);

                        return new GetStiffenerTakeoffResponse(
                            true,
                            report,
                            null,
                            report.HasTakeoff
                                ? "Calculated stiffener takeoff."
                                : "No stiffeners were required for the current ready rows and settings.");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        return GetStiffenerTakeoffResponse.Failure("stiffener-takeoff-failed", ex.Message);
                    }
                });
        }

        dispatcher.Register<UpdateProjectMetadataRequest>(
            BridgeMessageTypes.UpdateProjectMetadata,
            async (request, cancellationToken) =>
            {
                var result = await projectService
                    .UpdateMetadataAsync(
                        request.Project,
                        request.Metadata,
                        request.Settings ?? request.Project.Settings,
                        cancellationToken)
                    .ConfigureAwait(false);

                return result.Success && result.Project is not null
                    ? new UpdateProjectMetadataResponse(
                        true,
                        result.Project,
                        result.Project.Metadata,
                        result.Project.Settings,
                        null,
                        $"Updated metadata for '{result.Project.Metadata.ProjectName}'.")
                    : UpdateProjectMetadataResponse.Failure(
                        GetFirstErrorCode(result.Errors, "project-update-failed"),
                        GetFirstErrorMessage(result.Errors, "Project metadata could not be updated."));
            });

        dispatcher.Register<ChangeProjectKindRequest>(
            BridgeMessageTypes.ChangeProjectKind,
            async (request, cancellationToken) =>
            {
                var result = await projectService
                    .ChangeKindAsync(request.Project, request.ProjectKind, cancellationToken)
                    .ConfigureAwait(false);

                return result.Success && result.Project is not null
                    ? new ChangeProjectKindResponse(
                        true,
                        result.Project,
                        null,
                        $"Changed Project Kind to {(result.Project.ProjectKind == ProjectKind.Sheet ? "Sheet Project" : "Stock-Length Project")}.")
                    : ChangeProjectKindResponse.Failure(
                        GetFirstErrorCode(result.Errors, "project-kind-change-failed"),
                        GetFirstErrorMessage(result.Errors, "Project Kind could not be changed."));
            });

        dispatcher.Register<UpdateOptimizationGroupsRequest>(
            BridgeMessageTypes.UpdateOptimizationGroups,
            async (request, cancellationToken) =>
            {
                var result = await projectService
                    .UpdateOptimizationGroupsAsync(
                        request.Project,
                        request.Change,
                        cancellationToken)
                    .ConfigureAwait(false);

                return result.Success && result.Project is not null
                    ? new UpdateOptimizationGroupsResponse(
                        true,
                        result.Project,
                        null,
                        "Updated Optimization Groups.")
                    : UpdateOptimizationGroupsResponse.Failure(
                        GetFirstErrorCode(result.Errors, "optimization-group-change-invalid"),
                        GetFirstErrorMessage(result.Errors, "Optimization Groups could not be updated."));
            });

        dispatcher.Register<UpdateRequiredPiecesRequest>(
            BridgeMessageTypes.UpdateRequiredPieces,
            async (request, cancellationToken) =>
            {
                var result = await projectService
                    .UpdateRequiredPiecesAsync(request.Project, request.Change, cancellationToken)
                    .ConfigureAwait(false);

                return result.Success && result.Project is not null
                    ? new UpdateRequiredPiecesResponse(
                        true,
                        result.Project,
                        null,
                        "Updated Required Pieces.")
                    : UpdateRequiredPiecesResponse.Failure(
                        GetFirstErrorCode(result.Errors, "required-piece-change-invalid"),
                        GetFirstErrorMessage(result.Errors, "Required Pieces could not be updated."));
            });

        var stockLengthGenerationService = new StockLengthProjectGenerationService(
            new SheetOptimizerStockLengthCutPlanGenerator(nestingService));
        var oversizedStockAssignmentService = new OversizedStockAssignmentService();
        dispatcher.Register<SetOversizedStockRequest>(
            BridgeMessageTypes.SetOversizedStock,
            async (request, cancellationToken) =>
            {
                var result = await oversizedStockAssignmentService.SetAsync(
                        request.Project,
                        request.OptimizationGroupId,
                        request.OversizedStockLength,
                        cancellationToken)
                    .ConfigureAwait(false);
                var updated = result.Project?.State.OptimizationGroups.FirstOrDefault(group =>
                    string.Equals(group.OptimizationGroupId, request.OptimizationGroupId, StringComparison.Ordinal))
                    ?.LastStockLengthOptimizationResult;
                return result.Success && result.Project is not null && updated is not null
                    ? new SetOversizedStockResponse(
                        true,
                        result.Project,
                        updated,
                        null,
                        request.OversizedStockLength is null ? "Removed Oversized Stock assignment." : "Assigned Oversized Stock.")
                    : SetOversizedStockResponse.Failure(
                        GetFirstErrorCode(result.Errors, "oversized-stock-assignment-failed"),
                        GetFirstErrorMessage(result.Errors, "Oversized Stock could not be assigned."));
            });
        dispatcher.Register<GenerateSelectedCutPlanRequest>(
            BridgeMessageTypes.GenerateSelectedCutPlan,
            async (request, cancellationToken) =>
            {
                var operationId = ResolveOperationId(request.OperationId);
                using var operation = cutPlanGeneration.Begin(operationId, cancellationToken);
                ProjectOperationResult result;
                try
                {
                    result = await Task.Run(
                        () => stockLengthGenerationService.GenerateSelectedAsync(
                            request.Project,
                            request.OptimizationGroupId,
                            operation,
                            operation.Token),
                        CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return GenerateSelectedCutPlanResponse.Failure(
                        request.Project,
                        "cut-plan-generation-cancelled",
                        "Cut Plan generation was cancelled.");
                }
                var generated = result.Project?.State.OptimizationGroups.FirstOrDefault(group =>
                    string.Equals(group.OptimizationGroupId, request.OptimizationGroupId, StringComparison.Ordinal))
                    ?.LastStockLengthOptimizationResult;
                return result.Success && result.Project is not null && generated is not null
                    ? new GenerateSelectedCutPlanResponse(
                        true,
                        result.Project,
                        generated,
                        null,
                        $"Generated deterministic heuristic Cut Plan for '{request.OptimizationGroupId}'.")
                    : result.Project is not null
                        ? GenerateSelectedCutPlanResponse.Failure(
                            result.Project,
                            GetFirstErrorCode(result.Errors, "cut-plan-generation-failed"),
                            GetFirstErrorMessage(result.Errors, "The selected Optimization Group could not generate a Cut Plan."))
                        : GenerateSelectedCutPlanResponse.Failure(
                            GetFirstErrorCode(result.Errors, "cut-plan-generation-failed"),
                            GetFirstErrorMessage(result.Errors, "The selected Optimization Group could not generate a Cut Plan."));
            });
        dispatcher.Register<GenerateAllStaleCutPlansRequest>(
            BridgeMessageTypes.GenerateAllStaleCutPlans,
            async (request, cancellationToken) =>
            {
                var operationId = ResolveOperationId(request.OperationId);
                using var operation = cutPlanGeneration.Begin(operationId, cancellationToken);
                var result = await Task.Run(
                    () => stockLengthGenerationService.GenerateAllStaleAsync(
                        request.Project,
                        operation,
                        operation.Token),
                    CancellationToken.None)
                    .ConfigureAwait(false);
                var requestedGroupIds = request.Project.State.OptimizationGroups
                    .Where(group =>
                        group.RequiredPieces.Count > 0 &&
                        group.ResultStatus != OptimizationResultStatus.Valid)
                    .Select(group => group.OptimizationGroupId)
                    .ToHashSet(StringComparer.Ordinal);
                var completed = result.Project.State.OptimizationGroups.Count(group =>
                    requestedGroupIds.Contains(group.OptimizationGroupId) &&
                    group.ResultStatus == OptimizationResultStatus.Valid);
                var remaining = requestedGroupIds.Count - completed;
                var message = result.Success
                    ? "Generated every stale Optimization Group."
                    : $"Generated {completed} Optimization Group(s); " +
                      string.Join("; ", result.Failures.Select(failure =>
                          $"{failure.OptimizationGroupId}: {failure.Message}")) +
                      $" {remaining} Optimization Group(s) still need generation.";
                return new GenerateAllStaleCutPlansResponse(
                    result.Success,
                    result.Project,
                    result.Failures,
                    message);
            });
        dispatcher.Register<GenerateSelectedCutPlansRequest>(
            BridgeMessageTypes.GenerateSelectedCutPlans,
            async (request, cancellationToken) =>
            {
                var operationId = ResolveOperationId(request.OperationId);
                using var operation = cutPlanGeneration.Begin(operationId, cancellationToken);
                var orderedGroupIds = request.OptimizationGroupIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var project = request.Project;
                var failures = new List<StockLengthGenerationFailure>();
                var completed = 0;
                foreach (var optimizationGroupId in orderedGroupIds)
                {
                    try
                    {
                        var result = await Task.Run(
                            () => stockLengthGenerationService.GenerateSelectedAsync(
                                project,
                                optimizationGroupId,
                                operation,
                                operation.Token),
                            CancellationToken.None)
                            .ConfigureAwait(false);
                        if (result.Project is not null)
                        {
                            project = result.Project;
                        }
                        if (!result.Success)
                        {
                            failures.Add(new StockLengthGenerationFailure
                            {
                                OptimizationGroupId = optimizationGroupId,
                                Code = GetFirstErrorCode(result.Errors, "cut-plan-generation-failed"),
                                Message = GetFirstErrorMessage(result.Errors, "The Optimization Group could not generate a Cut Plan.")
                            });
                        }
                        else
                        {
                            completed++;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        failures.Add(new StockLengthGenerationFailure
                        {
                            OptimizationGroupId = optimizationGroupId,
                            Code = "cut-plan-generation-cancelled",
                            Message = "Cut Plan generation was cancelled."
                        });
                        break;
                    }
                }
                var message = failures.Count == 0
                    ? $"Generated {completed} selected Optimization Group(s)."
                    : $"Generated {completed} selected Optimization Group(s); " +
                      string.Join("; ", failures.Select(failure =>
                          $"{failure.OptimizationGroupId}: {failure.Message}"));
                return new GenerateAllStaleCutPlansResponse(
                    failures.Count == 0,
                    project,
                    failures,
                    message);
            });
        dispatcher.Register<CancelCutPlanGenerationRequest>(
            BridgeMessageTypes.CancelCutPlanGeneration,
            (request, _) =>
            {
                var requested = cutPlanGeneration.Cancel(request.OperationId);
                return Task.FromResult<object?>(new CancelCutPlanGenerationResponse(
                    true,
                    request.OperationId,
                    requested,
                    null,
                    requested
                        ? "Cut Plan cancellation requested."
                        : "No active Cut Plan generation operation was found."));
            });
        dispatcher.Register<GetCutPlanGenerationProgressRequest>(
            BridgeMessageTypes.GetCutPlanGenerationProgress,
            (request, _) =>
            {
                var progress = cutPlanGeneration.GetProgress(request.OperationId);
                return Task.FromResult<object?>(new GetCutPlanGenerationProgressResponse(
                    progress is not null,
                    request.OperationId,
                    progress,
                    progress is null
                        ? BridgeError.Create(
                            "cut-plan-generation-not-found",
                            "No active Cut Plan generation operation was found.")
                        : null,
                    null));
            });

        if (batchNestingService is not null &&
            reportDataService is not null &&
            pdfReportExporter is not null)
        {
            dispatcher.Register<UpdateReportSettingsRequest>(
                BridgeMessageTypes.UpdateReportSettings,
                async (request, cancellationToken) =>
                {
                    var result = await projectService
                        .UpdateMetadataAsync(
                            request.Project,
                            request.Project.Metadata,
                            request.Project.Settings with
                            {
                                ReportSettings = request.ReportSettings ?? new ReportSettings()
                            },
                            cancellationToken)
                        .ConfigureAwait(false);

                    return result.Success && result.Project is not null
                        ? new UpdateReportSettingsResponse(
                            true,
                            result.Project,
                            result.Project.Settings.ReportSettings,
                            null,
                            $"Updated report settings for '{result.Project.Metadata.ProjectName}'.")
                        : UpdateReportSettingsResponse.Failure(
                            GetFirstErrorCode(result.Errors, "report-settings-update-failed"),
                            GetFirstErrorMessage(result.Errors, "Report settings could not be updated."));
                });
        }

        dispatcher.Register<ImportRequest>(
            BridgeMessageTypes.ImportCsv,
            async (request, cancellationToken) =>
                await importService.ImportAsync(request, cancellationToken).ConfigureAwait(false));

        dispatcher.Register<UpdatePartRowRequest>(
            BridgeMessageTypes.UpdatePartRow,
            async (request, cancellationToken) =>
                await partEditorService
                    .UpdateRowAsync(
                        GetParts(request.Parts),
                        GetPartUpdate(request.Part),
                        cancellationToken)
                    .ConfigureAwait(false));

        dispatcher.Register<DeletePartRowRequest>(
            BridgeMessageTypes.DeletePartRow,
            async (request, cancellationToken) =>
                await partEditorService
                    .DeleteRowAsync(
                        GetParts(request.Parts),
                        request.RowId ?? string.Empty,
                        cancellationToken)
                    .ConfigureAwait(false));

        dispatcher.Register<AddPartRowRequest>(
            BridgeMessageTypes.AddPartRow,
            async (request, cancellationToken) =>
                await partEditorService
                    .AddRowAsync(
                        GetParts(request.Parts),
                        GetPartUpdate(request.Part),
                        cancellationToken)
                    .ConfigureAwait(false));

        dispatcher.Register<ListMaterialsRequest>(
            BridgeMessageTypes.ListMaterials,
            async (_, cancellationToken) =>
            {
                var libraryLocation = materialLibraryLocationService is null
                    ? null
                    : await materialLibraryLocationService.GetLocationAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    var materials = await materialService.ListAsync(cancellationToken).ConfigureAwait(false);
                    return new ListMaterialsResponse(
                        true,
                        materials,
                        null,
                        $"Loaded {materials.Count} material(s).",
                        libraryLocation);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (InvalidDataException ex)
                {
                    return ListMaterialsResponse.Failure(
                        "material-library-load-failed",
                        ex.Message,
                        "The material library is unreadable. Choose another library or repair the default library.",
                        libraryLocation);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return ListMaterialsResponse.Failure(
                        "material-library-access-failed",
                        ex.Message,
                        "OptiFab cannot access the material library. Choose another location or check the folder permissions.",
                        libraryLocation);
                }
            });

        dispatcher.Register<GetMaterialRequest>(
            BridgeMessageTypes.GetMaterial,
            async (request, cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.MaterialId))
                {
                    return GetMaterialResponse.Failure("material-id-required", "A materialId is required.");
                }

                var result = await materialService.GetAsync(request.MaterialId, cancellationToken).ConfigureAwait(false);
                return result.Success && result.Material is not null
                    ? new GetMaterialResponse(true, result.Material, null, $"Loaded material '{result.Material.Name}'.")
                    : GetMaterialResponse.Failure(
                        GetFirstErrorCode(result.Errors, "material-not-found"),
                        GetFirstErrorMessage(result.Errors, $"Material '{request.MaterialId}' was not found."));
            });

        dispatcher.Register<CreateMaterialRequest>(
            BridgeMessageTypes.CreateMaterial,
            async (request, cancellationToken) =>
            {
                var result = await materialService.CreateAsync(request.Material, cancellationToken).ConfigureAwait(false);
                return result.Success && result.Material is not null
                    ? new CreateMaterialResponse(true, result.Material, null, $"Created material '{result.Material.Name}'.")
                    : CreateMaterialResponse.Failure(
                        GetFirstErrorCode(result.Errors, "material-create-failed"),
                        GetFirstErrorMessage(result.Errors, "Material could not be created."));
            });

        dispatcher.Register<UpdateMaterialRequest>(
            BridgeMessageTypes.UpdateMaterial,
            async (request, cancellationToken) =>
            {
                var result = await materialService.UpdateAsync(request.Material, cancellationToken).ConfigureAwait(false);
                return result.Success && result.Material is not null
                    ? new UpdateMaterialResponse(true, result.Material, null, $"Updated material '{result.Material.Name}'.")
                    : UpdateMaterialResponse.Failure(
                        GetFirstErrorCode(result.Errors, "material-update-failed"),
                        GetFirstErrorMessage(result.Errors, "Material could not be updated."));
            });

        dispatcher.Register<DeleteMaterialRequest>(
            BridgeMessageTypes.DeleteMaterial,
            async (request, cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.MaterialId))
                {
                    return DeleteMaterialResponse.Failure(string.Empty, "material-id-required", "A materialId is required.");
                }

                var existingMaterialResult = await materialService
                    .GetAsync(request.MaterialId, cancellationToken)
                    .ConfigureAwait(false);
                if (!existingMaterialResult.Success || existingMaterialResult.Material is null)
                {
                    return DeleteMaterialResponse.Failure(
                        request.MaterialId,
                        GetFirstErrorCode(existingMaterialResult.Errors, "material-not-found"),
                        GetFirstErrorMessage(existingMaterialResult.Errors, $"Material '{request.MaterialId}' was not found."));
                }

                var isInUse = IsMaterialInUse(request, existingMaterialResult.Material);
                var result = await materialService
                    .DeleteAsync(request.MaterialId, isInUse, cancellationToken)
                    .ConfigureAwait(false);

                return result.Success
                    ? new DeleteMaterialResponse(
                        true,
                        request.MaterialId,
                        null,
                        $"Deleted material '{existingMaterialResult.Material.Name}'.")
                    : DeleteMaterialResponse.Failure(
                        request.MaterialId,
                        GetFirstErrorCode(result.Errors, "material-delete-failed"),
                        GetFirstErrorMessage(result.Errors, $"Material '{request.MaterialId}' could not be deleted."));
            });

        if (materialLibraryLocationService is not null)
        {
            dispatcher.Register<ChooseMaterialLibraryLocationRequest>(
                BridgeMessageTypes.ChooseMaterialLibraryLocation,
                async (_, cancellationToken) =>
                {
                    SaveFileDialogResponse dialogResult;
                    try
                    {
                        var currentLocation = await materialLibraryLocationService
                            .GetLocationAsync(cancellationToken)
                            .ConfigureAwait(false);
                        dialogResult = await fileDialogService
                            .SaveAsync(
                                new SaveFileDialogRequest(
                                    "Choose material library location",
                                    BuildMaterialLibraryFileName(currentLocation),
                                    MaterialLibraryFileFilters,
                                    ".json",
                                    false),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (InvalidDataException ex)
                    {
                        return ChooseMaterialLibraryLocationResponse.Failure(
                            "material-library-load-failed",
                            ex.Message);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        return ChooseMaterialLibraryLocationResponse.Failure(
                            "material-library-location-update-failed",
                            ex.Message);
                    }

                    if (!dialogResult.Success || string.IsNullOrWhiteSpace(dialogResult.FilePath))
                    {
                        return ChooseMaterialLibraryLocationResponse.Cancelled();
                    }

                    try
                    {
                        var location = await materialLibraryLocationService
                            .RepointAsync(dialogResult.FilePath, cancellationToken)
                            .ConfigureAwait(false);
                        var materials = await materialService.ListAsync(cancellationToken).ConfigureAwait(false);
                        return new ChooseMaterialLibraryLocationResponse(
                            true,
                            materials,
                            location,
                            null,
                            $"Material library now points to '{location.ActiveFilePath}'.");
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (ArgumentException ex)
                    {
                        return ChooseMaterialLibraryLocationResponse.Failure(
                            "material-library-invalid-path",
                            ex.Message);
                    }
                    catch (InvalidDataException ex)
                    {
                        return ChooseMaterialLibraryLocationResponse.Failure(
                            "material-library-load-failed",
                            ex.Message);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        return ChooseMaterialLibraryLocationResponse.Failure(
                            "material-library-location-update-failed",
                            ex.Message);
                    }
                });

            dispatcher.Register<RestoreDefaultMaterialLibraryLocationRequest>(
                BridgeMessageTypes.RestoreDefaultMaterialLibraryLocation,
                async (_, cancellationToken) =>
                {
                    try
                    {
                        var previousLocation = await materialLibraryLocationService
                            .GetLocationAsync(cancellationToken)
                            .ConfigureAwait(false);
                        var defaultFileExisted = File.Exists(previousLocation.DefaultFilePath);
                        var preservedLibraryCount = CountPreservedMaterialLibraries(previousLocation.DefaultFilePath);
                        var location = await materialLibraryLocationService
                            .RestoreDefaultAsync(cancellationToken)
                            .ConfigureAwait(false);
                        var materials = await materialService.ListAsync(cancellationToken).ConfigureAwait(false);
                        var defaultLibraryWasRepaired =
                            CountPreservedMaterialLibraries(location.DefaultFilePath) > preservedLibraryCount;
                        var responseMessage = defaultLibraryWasRepaired
                            ? "Default material library repaired. The unreadable library was preserved beside the new materials.json file."
                            : defaultFileExisted
                            ? "Material library restored to the default location."
                            : $"Default material library was recreated at '{location.DefaultFilePath}'.";

                        return new RestoreDefaultMaterialLibraryLocationResponse(
                            true,
                            materials,
                            location,
                            null,
                            responseMessage);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (InvalidDataException ex)
                    {
                        return RestoreDefaultMaterialLibraryLocationResponse.Failure(
                            "material-library-load-failed",
                            ex.Message);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        return RestoreDefaultMaterialLibraryLocationResponse.Failure(
                            "material-library-restore-failed",
                            ex.Message);
                    }
                });
        }

        dispatcher.Register<NestRequest>(
            BridgeMessageTypes.RunNesting,
            async (request, cancellationToken) =>
                await nestingService.NestAsync(request, cancellationToken).ConfigureAwait(false));

        if (batchNestingService is not null &&
            reportDataService is not null &&
            pdfReportExporter is not null)
        {
            dispatcher.Register<BatchNestRequest>(
                BridgeMessageTypes.RunBatchNesting,
                async (request, cancellationToken) =>
                    await batchNestingService.NestBatchAsync(request, cancellationToken).ConfigureAwait(false));

            dispatcher.Register<ExportPdfReportRequest>(
                BridgeMessageTypes.ExportPdfReport,
                async (request, cancellationToken) =>
                {
                    var filePath = NormalizeFilePath(request.FilePath);
                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        var dialogResult = await fileDialogService
                            .SaveAsync(
                                new SaveFileDialogRequest(
                                    "Export OptiFab PDF report",
                                    string.IsNullOrWhiteSpace(request.SuggestedFileName)
                                        ? BuildPdfFileName(request.Project)
                                        : BuildPdfFileName(request.Project, request.SuggestedFileName),
                                    PdfFileFilters,
                                    ".pdf"),
                                cancellationToken)
                            .ConfigureAwait(false);

                        if (!dialogResult.Success || string.IsNullOrWhiteSpace(dialogResult.FilePath))
                        {
                            return ExportPdfReportResponse.Cancelled();
                        }

                        filePath = dialogResult.FilePath;
                    }

                    try
                    {
                        var logoPath = ResolveCompanyLogoPath(request.CompanyLogoPath, desktopAppSettingsStore);
                        if (request.Project.ProjectKind == ProjectKind.StockLength)
                        {
                            var stockLengthReport = await new StockLengthReportDataService()
                                .BuildAsync(
                                    new StockLengthReportDataRequest
                                    {
                                        Project = request.Project,
                                        Scope = request.StockLengthScope ?? new StockLengthReportScope()
                                    },
                                    cancellationToken)
                                .ConfigureAwait(false);
                            stockLengthReport = stockLengthReport with { CompanyLogoPath = logoPath };
                            await new QuestPdfStockLengthReportExporter()
                                .ExportAsync(stockLengthReport, filePath, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            var reportData = await reportDataService
                                .BuildReportDataAsync(
                                    new ReportDataRequest
                                    {
                                        Project = request.Project,
                                        BatchResult = request.BatchResult
                                    },
                                    cancellationToken)
                                .ConfigureAwait(false);
                            reportData = reportData with { CompanyLogoPath = logoPath };
                            await pdfReportExporter
                                .ExportAsync(reportData, filePath, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        var message = $"Exported PDF report to '{Path.GetFileName(filePath)}'.";

                        try
                        {
                            (exportedPdfOpener ?? OpenExportedPdf)(filePath);
                            message = $"Exported and opened PDF report '{Path.GetFileName(filePath)}'.";
                        }
                        catch (Exception ex)
                        {
                            message = $"{message} Could not open it automatically: {ex.Message}";
                        }

                        return new ExportPdfReportResponse(
                            true,
                            filePath,
                            null,
                            message);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        return ExportPdfReportResponse.Failure(filePath, "report-export-failed", ex.Message);
                    }
                });
        }

        if (batchNestingService is not null &&
            reportDataService is not null &&
            excelReportExporter is not null)
        {
            dispatcher.Register<ExportExcelReportRequest>(
                BridgeMessageTypes.ExportExcelReport,
                async (request, cancellationToken) =>
                {
                    var filePath = NormalizeFilePath(request.FilePath);
                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        var dialogResult = await fileDialogService
                            .SaveAsync(
                                new SaveFileDialogRequest(
                                    "Export OptiFab Excel report",
                                    string.IsNullOrWhiteSpace(request.SuggestedFileName)
                                        ? BuildExcelFileName(request.Project)
                                        : BuildExcelFileName(request.Project, request.SuggestedFileName),
                                    ExcelFileFilters,
                                    ".xlsx"),
                                cancellationToken)
                            .ConfigureAwait(false);

                        if (!dialogResult.Success || string.IsNullOrWhiteSpace(dialogResult.FilePath))
                        {
                            return ExportExcelReportResponse.Cancelled();
                        }

                        filePath = dialogResult.FilePath;
                    }

                    try
                    {
                        if (request.Project.ProjectKind == ProjectKind.StockLength)
                        {
                            if (excelReportExporter is not IStockLengthExcelReportExporter stockLengthExporter)
                            {
                                return ExportExcelReportResponse.Failure(
                                    filePath,
                                    "stock-length-excel-export-unsupported",
                                    "The configured Excel exporter does not support Stock-Length Projects.");
                            }

                            var stockLengthReport = await new StockLengthReportDataService()
                                .BuildAsync(
                                    new StockLengthReportDataRequest
                                    {
                                        Project = request.Project,
                                        Scope = request.StockLengthScope ?? new StockLengthReportScope()
                                    },
                                    cancellationToken)
                                .ConfigureAwait(false);
                            await stockLengthExporter
                                .ExportAsync(stockLengthReport, filePath, cancellationToken)
                                .ConfigureAwait(false);

                            return new ExportExcelReportResponse(
                                true,
                                filePath,
                                null,
                                $"Exported Stock-Length Excel report to '{Path.GetFileName(filePath)}'.");
                        }

                        var reportData = await reportDataService
                            .BuildReportDataAsync(
                                new ReportDataRequest
                                {
                                    Project = request.Project,
                                    BatchResult = request.BatchResult
                                },
                                cancellationToken)
                            .ConfigureAwait(false);

                        await excelReportExporter
                            .ExportAsync(reportData, filePath, cancellationToken)
                            .ConfigureAwait(false);

                        var message = $"Exported Excel report to '{Path.GetFileName(filePath)}'.";

                        try
                        {
                            OpenExportedFile(filePath);
                            message = $"Exported and opened Excel report '{Path.GetFileName(filePath)}'.";
                        }
                        catch (Exception ex)
                        {
                            message = $"{message} Could not open it automatically: {ex.Message}";
                        }

                        return new ExportExcelReportResponse(
                            true,
                            filePath,
                            null,
                            message);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        return ExportExcelReportResponse.Failure(
                            filePath,
                            "report-excel-export-failed",
                            ex.Message);
                    }
                });
        }

        if (stiffenerTakeoffService is not null &&
            stiffenerPdfReportExporter is not null)
        {
            dispatcher.Register<ExportStiffenerPdfReportRequest>(
                BridgeMessageTypes.ExportStiffenerPdfReport,
                async (request, cancellationToken) =>
                {
                    var filePath = NormalizeFilePath(request.FilePath);
                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        var dialogResult = await fileDialogService
                            .SaveAsync(
                                new SaveFileDialogRequest(
                                    "Export OptiFab stiffener PDF report",
                                    string.IsNullOrWhiteSpace(request.SuggestedFileName)
                                        ? BuildStiffenerPdfFileName(request.Project)
                                        : BuildStiffenerPdfFileName(request.Project, request.SuggestedFileName),
                                    PdfFileFilters,
                                    ".pdf"),
                                cancellationToken)
                            .ConfigureAwait(false);

                        if (!dialogResult.Success || string.IsNullOrWhiteSpace(dialogResult.FilePath))
                        {
                            return ExportStiffenerPdfReportResponse.Cancelled();
                        }

                        filePath = dialogResult.FilePath;
                    }

                    try
                    {
                        var report = await stiffenerTakeoffService
                            .BuildAsync(
                                new StiffenerTakeoffRequest
                                {
                                    Project = request.Project
                                },
                                cancellationToken)
                            .ConfigureAwait(false);
                        var logoPath = ResolveCompanyLogoPath(request.CompanyLogoPath, desktopAppSettingsStore);
                        report = report with
                        {
                            CompanyLogoPath = logoPath
                        };

                        if (!report.Settings.Enabled)
                        {
                            return ExportStiffenerPdfReportResponse.Failure(
                                filePath,
                                "stiffener-report-disabled",
                                "Stiffener takeoff is disabled for this project.");
                        }

                        await stiffenerPdfReportExporter
                            .ExportAsync(report, filePath, cancellationToken)
                            .ConfigureAwait(false);

                        var message = $"Exported stiffener PDF report to '{Path.GetFileName(filePath)}'.";

                        try
                        {
                            (exportedPdfOpener ?? OpenExportedPdf)(filePath);
                            message = $"Exported and opened stiffener PDF report '{Path.GetFileName(filePath)}'.";
                        }
                        catch (Exception ex)
                        {
                            message = $"{message} Could not open it automatically: {ex.Message}";
                        }

                        return new ExportStiffenerPdfReportResponse(
                            true,
                            filePath,
                            null,
                            message);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        return ExportStiffenerPdfReportResponse.Failure(
                            filePath,
                            "stiffener-report-export-failed",
                            ex.Message);
                    }
                });
        }

        var extrusionTakeoffService = new ExtrusionTakeoffService();
        var extrusionPdfReportExporter = new QuestPdfExtrusionReportExporter();
        var extrusionExcelReportExporter = new ClosedXmlExtrusionReportExporter();

        dispatcher.Register<GetExtrusionLayoutRequest>(
            BridgeMessageTypes.GetExtrusionLayout,
            async (request, cancellationToken) =>
            {
                try
                {
                    var layout = await extrusionTakeoffService
                        .BuildLayoutAsync(
                            new ExtrusionLayoutRequest
                            {
                                Project = request.Project
                            },
                            cancellationToken)
                        .ConfigureAwait(false);

                    return new GetExtrusionLayoutResponse(
                        true,
                        layout,
                        null,
                        "Prepared extrusion layout.");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return GetExtrusionLayoutResponse.Failure("extrusion-layout-failed", ex.Message);
                }
            });

        dispatcher.Register<UpdateExtrusionLayoutRequest>(
            BridgeMessageTypes.UpdateExtrusionLayout,
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var project = request.Project with
                {
                    State = request.Project.State with
                    {
                        ExtrusionLayout = request.Layout ?? new ExtrusionLayoutState()
                    }
                };

                return Task.FromResult<object?>(
                    new UpdateExtrusionLayoutResponse(
                        true,
                        project,
                        project.State.ExtrusionLayout,
                        null,
                        "Updated extrusion layout. Save the project to keep these changes."));
            });

        dispatcher.Register<GetExtrusionReportRequest>(
            BridgeMessageTypes.GetExtrusionReport,
            async (request, cancellationToken) =>
            {
                try
                {
                    var report = await extrusionTakeoffService
                        .BuildReportAsync(
                            new ExtrusionReportRequest
                            {
                                Project = request.Project
                            },
                            cancellationToken)
                        .ConfigureAwait(false);

                    return new GetExtrusionReportResponse(
                        true,
                        report,
                        null,
                        report.HasTakeoff
                            ? "Calculated extrusion report."
                            : "No extrusion segments have been assigned for this layout.");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return GetExtrusionReportResponse.Failure("extrusion-report-failed", ex.Message);
                }
            });

        dispatcher.Register<ExportExtrusionPdfReportRequest>(
            BridgeMessageTypes.ExportExtrusionPdfReport,
            async (request, cancellationToken) =>
            {
                var filePath = NormalizeFilePath(request.FilePath);
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    var dialogResult = await fileDialogService
                        .SaveAsync(
                            new SaveFileDialogRequest(
                                "Export OptiFab extrusion PDF report",
                                string.IsNullOrWhiteSpace(request.SuggestedFileName)
                                    ? BuildExtrusionPdfFileName(request.Project)
                                    : BuildExtrusionPdfFileName(request.Project, request.SuggestedFileName),
                                PdfFileFilters,
                                ".pdf"),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (!dialogResult.Success || string.IsNullOrWhiteSpace(dialogResult.FilePath))
                    {
                        return ExportExtrusionPdfReportResponse.Cancelled();
                    }

                    filePath = dialogResult.FilePath;
                }

                try
                {
                    var report = await extrusionTakeoffService
                        .BuildReportAsync(new ExtrusionReportRequest { Project = request.Project }, cancellationToken)
                        .ConfigureAwait(false);
                    report = report with
                    {
                        CompanyLogoPath = ResolveCompanyLogoPath(request.CompanyLogoPath, desktopAppSettingsStore)
                    };

                    await extrusionPdfReportExporter.ExportAsync(report, filePath, cancellationToken).ConfigureAwait(false);
                    var message = $"Exported extrusion PDF report to '{Path.GetFileName(filePath)}'.";

                    try
                    {
                        (exportedPdfOpener ?? OpenExportedPdf)(filePath);
                        message = $"Exported and opened extrusion PDF report '{Path.GetFileName(filePath)}'.";
                    }
                    catch (Exception ex)
                    {
                        message = $"{message} Could not open it automatically: {ex.Message}";
                    }

                    return new ExportExtrusionPdfReportResponse(true, filePath, null, message);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return ExportExtrusionPdfReportResponse.Failure(filePath, "extrusion-report-export-failed", ex.Message);
                }
            });

        dispatcher.Register<ExportExtrusionExcelReportRequest>(
            BridgeMessageTypes.ExportExtrusionExcelReport,
            async (request, cancellationToken) =>
            {
                var filePath = NormalizeFilePath(request.FilePath);
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    var dialogResult = await fileDialogService
                        .SaveAsync(
                            new SaveFileDialogRequest(
                                "Export OptiFab extrusion Excel report",
                                string.IsNullOrWhiteSpace(request.SuggestedFileName)
                                    ? BuildExtrusionExcelFileName(request.Project)
                                    : BuildExtrusionExcelFileName(request.Project, request.SuggestedFileName),
                                ExcelFileFilters,
                                ".xlsx"),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (!dialogResult.Success || string.IsNullOrWhiteSpace(dialogResult.FilePath))
                    {
                        return ExportExtrusionExcelReportResponse.Cancelled();
                    }

                    filePath = dialogResult.FilePath;
                }

                try
                {
                    var report = await extrusionTakeoffService
                        .BuildReportAsync(new ExtrusionReportRequest { Project = request.Project }, cancellationToken)
                        .ConfigureAwait(false);
                    await extrusionExcelReportExporter.ExportAsync(report, filePath, cancellationToken).ConfigureAwait(false);
                    OpenExportedFile(filePath);
                    return new ExportExtrusionExcelReportResponse(
                        true,
                        filePath,
                        null,
                        $"Exported extrusion Excel report to '{Path.GetFileName(filePath)}'.");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return ExportExtrusionExcelReportResponse.Failure(filePath, "extrusion-report-export-failed", ex.Message);
                }
            });

        return dispatcher;
    }

    public static BridgeMessageDispatcher CreateDefault(
        IFileDialogService fileDialogService,
        IMaterialService materialService,
        IProjectService projectService,
        IImportService importService,
        IPartEditorService partEditorService,
        INestingService nestingService,
        IBatchNestingService? batchNestingService,
        IReportDataService? reportDataService,
        IPdfReportExporter? pdfReportExporter,
        IStiffenerTakeoffService? stiffenerTakeoffService,
        IStiffenerPdfReportExporter? stiffenerPdfReportExporter,
        Func<WebUiContentLocation> contentLocationAccessor,
        DesktopAppSettingsStore? desktopAppSettingsStore = null,
        IMaterialLibraryLocationService? materialLibraryLocationService = null,
        Action<string>? exportedPdfOpener = null) =>
        CreateDefault(
            fileDialogService,
            materialService,
            projectService,
            importService,
            partEditorService,
            nestingService,
            batchNestingService,
            reportDataService,
            pdfReportExporter,
            excelReportExporter: null,
            stiffenerTakeoffService,
            stiffenerPdfReportExporter,
            contentLocationAccessor,
            desktopAppSettingsStore,
            materialLibraryLocationService,
            exportedPdfOpener);

    private static bool IsMaterialInUse(DeleteMaterialRequest request, Material material) =>
        request.ImportedMaterialNames?.Any(name =>
            string.Equals(name, material.Name, StringComparison.Ordinal)) == true;

    private static readonly IReadOnlyList<FileDialogFilter> ProjectFileFilters =
    [
        new FileDialogFilter("OptiFab project files", ["pnest"]),
        new FileDialogFilter("All files", ["*.*"])
    ];

    private static readonly IReadOnlyList<FileDialogFilter> ImportFileFilters =
    [
        new FileDialogFilter("Supported import files", ["csv", "xlsx", "xlsm"]),
        new FileDialogFilter("CSV files", ["csv"]),
        new FileDialogFilter("Excel Workbooks", ["xlsx", "xlsm"]),
        new FileDialogFilter("All files", ["*.*"])
    ];

    private static readonly IReadOnlyList<FileDialogFilter> PdfFileFilters =
    [
        new FileDialogFilter("PDF files", ["pdf"]),
        new FileDialogFilter("All files", ["*.*"])
    ];

    private static readonly IReadOnlyList<FileDialogFilter> ExcelFileFilters =
    [
        new FileDialogFilter("Excel workbooks", ["xlsx"]),
        new FileDialogFilter("All files", ["*.*"])
    ];

    private static DesktopAppSettingsPayload ToPayload(DesktopAppSettings settings) =>
        new(settings.CompanyLogoPath, settings.CompanyName);

    private static DesktopAppSettings NormalizeDesktopAppSettings(
        DesktopAppSettingsPayload? settings,
        DesktopAppSettings currentSettings)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);

        if (settings is null)
        {
            return currentSettings;
        }

        var companyLogoPath = ImportCompanyLogo(
            settings.CompanyLogoPath,
            currentSettings.CompanyLogoPath);
        return new DesktopAppSettings
        {
            ActiveMaterialLibraryPath = currentSettings.ActiveMaterialLibraryPath,
            CompanyLogoPath = companyLogoPath,
            CompanyName = NormalizeOptionalValue(settings.CompanyName)
        };
    }

    private static string? ImportCompanyLogo(string? requestedPath, string? currentLogoPath)
    {
        var normalizedRequested = NormalizeOptionalFilePath(requestedPath);
        var normalizedCurrent = NormalizeOptionalFilePath(currentLogoPath);

        if (string.IsNullOrWhiteSpace(normalizedRequested))
        {
            CleanupImportedCompanyLogos(exceptPath: null);
            return null;
        }

        if (!File.Exists(normalizedRequested))
        {
            throw new FileNotFoundException("The selected company logo could not be found.", normalizedRequested);
        }

        if (!string.IsNullOrWhiteSpace(normalizedCurrent) &&
            string.Equals(normalizedRequested, normalizedCurrent, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedCurrent;
        }

        var extension = Path.GetExtension(normalizedRequested);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".png";
        }

        Directory.CreateDirectory(DesktopStoragePaths.CompanyLogoDirectory);
        var destinationPath = Path.Combine(
            DesktopStoragePaths.CompanyLogoDirectory,
            $"company-logo{extension.ToLowerInvariant()}");

        File.Copy(normalizedRequested, destinationPath, overwrite: true);
        CleanupImportedCompanyLogos(destinationPath);
        return destinationPath;
    }

    private static void CleanupImportedCompanyLogos(string? exceptPath)
    {
        if (!Directory.Exists(DesktopStoragePaths.CompanyLogoDirectory))
        {
            return;
        }

        var normalizedException = NormalizeOptionalFilePath(exceptPath);
        foreach (var filePath in Directory.EnumerateFiles(
                     DesktopStoragePaths.CompanyLogoDirectory,
                     "company-logo.*",
                     SearchOption.TopDirectoryOnly))
        {
            var normalizedFilePath = NormalizeOptionalFilePath(filePath);
            if (!string.IsNullOrWhiteSpace(normalizedException) &&
                string.Equals(normalizedFilePath, normalizedException, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(filePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string? ResolveCompanyLogoPath(
        string? requestedPath,
        DesktopAppSettingsStore? desktopAppSettingsStore)
    {
        var normalizedRequested = NormalizeOptionalFilePath(requestedPath);
        if (!string.IsNullOrWhiteSpace(normalizedRequested))
        {
            return normalizedRequested;
        }

        return NormalizeOptionalFilePath(desktopAppSettingsStore?.Load().CompanyLogoPath);
    }

    private static void OpenExportedPdf(string filePath) =>
        OpenExportedFile(filePath);

    private static void OpenExportedFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        Process.Start(
            new ProcessStartInfo(filePath)
            {
                UseShellExecute = true
            });
    }

    private static readonly IReadOnlyList<FileDialogFilter> MaterialLibraryFileFilters =
    [
        new FileDialogFilter("Material library files", ["json"]),
        new FileDialogFilter("All files", ["*.*"])
    ];

    private static string BuildProjectFileName(Project project)
    {
        var rawName = string.IsNullOrWhiteSpace(project.Metadata.ProjectName)
            ? "optifab-project"
            : project.Metadata.ProjectName;
        var sanitized = string.Concat(rawName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)).Trim();
        var fileName = string.IsNullOrWhiteSpace(sanitized) ? "optifab-project" : sanitized;

        return fileName.EndsWith(".pnest", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.pnest";
    }

    private static string BuildPdfFileName(Project project, string? suggestedFileName = null)
    {
        var rawName = !string.IsNullOrWhiteSpace(suggestedFileName)
            ? suggestedFileName
            : !string.IsNullOrWhiteSpace(project.Settings.ReportSettings.ReportTitle)
                ? project.Settings.ReportSettings.ReportTitle
                : project.Metadata.ProjectName;
        var sanitized = string.Concat(rawName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)).Trim();
        var fileName = string.IsNullOrWhiteSpace(sanitized) ? "optifab-report" : sanitized;

        return fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.pdf";
    }

    private static string BuildExcelFileName(Project project, string? suggestedFileName = null)
    {
        var rawName = !string.IsNullOrWhiteSpace(suggestedFileName)
            ? suggestedFileName
            : !string.IsNullOrWhiteSpace(project.Settings.ReportSettings.ReportTitle)
                ? project.Settings.ReportSettings.ReportTitle
                : project.Metadata.ProjectName;
        var sanitized = string.Concat(rawName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)).Trim();
        var fileName = string.IsNullOrWhiteSpace(sanitized) ? "optifab-summary" : sanitized;

        return fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.xlsx";
    }

    private static string BuildStiffenerPdfFileName(Project project, string? suggestedFileName = null)
    {
        var rawName = !string.IsNullOrWhiteSpace(suggestedFileName)
            ? suggestedFileName
            : !string.IsNullOrWhiteSpace(project.Metadata.ProjectName)
                ? $"{project.Metadata.ProjectName} Stiffener Takeoff"
                : "optifab-stiffener-takeoff";
        var sanitized = string.Concat(rawName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)).Trim();
        var fileName = string.IsNullOrWhiteSpace(sanitized) ? "optifab-stiffener-takeoff" : sanitized;

        return fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.pdf";
    }

    private static string BuildExtrusionPdfFileName(Project project, string? suggestedFileName = null) =>
        BuildExportFileName(project, suggestedFileName, "Extrusion Takeoff", "optifab-extrusion-takeoff", ".pdf");

    private static string BuildExtrusionExcelFileName(Project project, string? suggestedFileName = null) =>
        BuildExportFileName(project, suggestedFileName, "Extrusion Takeoff", "optifab-extrusion-takeoff", ".xlsx");

    private static string BuildExportFileName(
        Project project,
        string? suggestedFileName,
        string suffix,
        string fallback,
        string extension)
    {
        var rawName = !string.IsNullOrWhiteSpace(suggestedFileName)
            ? suggestedFileName
            : !string.IsNullOrWhiteSpace(project.Metadata.ProjectName)
                ? $"{project.Metadata.ProjectName} {suffix}"
                : fallback;
        var sanitized = string.Concat(rawName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)).Trim();
        var fileName = string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;

        return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}{extension}";
    }

    private static string BuildMaterialLibraryFileName(MaterialLibraryLocation location)
    {
        var rawName = Path.GetFileName(string.IsNullOrWhiteSpace(location.ActiveFilePath)
            ? location.DefaultFilePath
            : location.ActiveFilePath);

        return string.IsNullOrWhiteSpace(rawName)
            ? "materials.json"
            : rawName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? rawName
                : $"{rawName}.json";
    }

    private static int CountPreservedMaterialLibraries(string defaultFilePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(defaultFilePath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return 0;
            }

            var fileName = Path.GetFileNameWithoutExtension(defaultFilePath);
            var extension = Path.GetExtension(defaultFilePath);
            return Directory.EnumerateFiles(directory, $"{fileName}.unreadable-*{extension}").Count();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static IReadOnlyList<string> GetCapabilities(
        BridgeHandshakeRequest request,
        BridgeMessageDispatcher dispatcher)
    {
        if (request.RequestedCapabilities is null || request.RequestedCapabilities.Count == 0)
        {
            return dispatcher.RegisteredTypes;
        }

        var supported = dispatcher.RegisteredTypes.ToHashSet(StringComparer.Ordinal);
        return request.RequestedCapabilities
            .Where(supported.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetHostVersion() =>
        typeof(DesktopBridgeRegistration).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private static string? NormalizeFilePath(string? filePath) =>
        string.IsNullOrWhiteSpace(filePath) ? null : filePath.Trim();

    private static bool IsExcelWorkbookPath(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOptionalFilePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        return Path.GetFullPath(filePath.Trim());
    }

    private static IReadOnlyList<PartRow> GetParts(IReadOnlyList<PartRow>? parts) =>
        parts ?? Array.Empty<PartRow>();

    private static PartRowUpdate GetPartUpdate(PartRowUpdate? update) =>
        update ?? new PartRowUpdate();

    private static async Task<ImportPreparationResult> PrepareImportOptionsAsync(
        ImportFileRequest request,
        IMaterialService materialService,
        CancellationToken cancellationToken)
    {
        var requestedOptions = request.Options ?? new ImportOptions();
        if (request.NewMaterials is not { Count: > 0 })
        {
            return new ImportPreparationResult(
                true,
                requestedOptions,
                new HashSet<string>(StringComparer.Ordinal),
                Array.Empty<Material>(),
                Array.Empty<ValidationError>());
        }

        var createdSourceMaterials = new List<string>(request.NewMaterials.Count);
        var createdMaterials = new List<Material>(request.NewMaterials.Count);
        var errors = new List<ValidationError>();
        var existingMappings = new Dictionary<string, ImportMaterialMapping>(StringComparer.Ordinal);

        foreach (var materialMapping in requestedOptions.MaterialMappings)
        {
            var sourceMaterialName = materialMapping.SourceMaterialName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sourceMaterialName))
            {
                errors.Add(new ValidationError("invalid-material-mapping", "Material mappings require a sourceMaterialName."));
                continue;
            }

            if (!existingMappings.TryAdd(sourceMaterialName, materialMapping))
            {
                errors.Add(new ValidationError(
                    "duplicate-material-mapping",
                    $"Import material '{sourceMaterialName}' was mapped more than once."));
            }
        }

        try
        {
            foreach (var newMaterial in request.NewMaterials)
            {
                var sourceMaterialName = newMaterial.SourceMaterialName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sourceMaterialName))
                {
                    errors.Add(new ValidationError("invalid-material-mapping", "New import materials require a sourceMaterialName."));
                    continue;
                }

                if (newMaterial.Material is null)
                {
                    errors.Add(new ValidationError(
                        "invalid-material-mapping",
                        $"New import material '{sourceMaterialName}' is missing a material definition."));
                    continue;
                }

                if (existingMappings.ContainsKey(sourceMaterialName))
                {
                    errors.Add(new ValidationError(
                        "duplicate-material-mapping",
                        $"Import material '{sourceMaterialName}' was mapped more than once."));
                    continue;
                }

                var createResult = await materialService.CreateAsync(newMaterial.Material, cancellationToken).ConfigureAwait(false);
                if (!createResult.Success || createResult.Material is null)
                {
                    if (createResult.Errors.Count > 0)
                    {
                        errors.AddRange(createResult.Errors);
                    }
                    else
                    {
                        errors.Add(new ValidationError(
                            "material-create-failed",
                            $"Material '{newMaterial.Material.Name}' could not be created for import."));
                    }

                    continue;
                }

                var createdMapping = new ImportMaterialMapping
                {
                    SourceMaterialName = sourceMaterialName,
                    TargetMaterialId = createResult.Material.MaterialId
                };
                existingMappings.Add(sourceMaterialName, createdMapping);
                createdSourceMaterials.Add(sourceMaterialName);
                createdMaterials.Add(createResult.Material);
            }
        }
        catch
        {
            await RollbackCreatedMaterialsAsync(materialService, createdMaterials).ConfigureAwait(false);
            throw;
        }

        if (errors.Count > 0)
        {
            await RollbackCreatedMaterialsAsync(materialService, createdMaterials).ConfigureAwait(false);
        }

        return errors.Count > 0
            ? new ImportPreparationResult(
                false,
                requestedOptions,
                createdSourceMaterials.ToHashSet(StringComparer.Ordinal),
                Array.Empty<Material>(),
                errors)
            : new ImportPreparationResult(
                true,
                requestedOptions with { MaterialMappings = existingMappings.Values.ToArray() },
                createdSourceMaterials.ToHashSet(StringComparer.Ordinal),
                createdMaterials,
                Array.Empty<ValidationError>());
    }

    private static async Task RollbackCreatedMaterialsAsync(
        IMaterialService materialService,
        IReadOnlyList<Material> createdMaterials)
    {
        foreach (var material in createdMaterials.Reverse())
        {
            await materialService.DeleteAsync(material.MaterialId, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static ImportResponse MarkCreatedMaterialResolutions(
        ImportResponse response,
        IReadOnlySet<string> createdSourceMaterials)
    {
        if (createdSourceMaterials.Count == 0 || response.MaterialResolutions.Count == 0)
        {
            return response;
        }

        return response with
        {
            MaterialResolutions = response.MaterialResolutions
                .Select(resolution => createdSourceMaterials.Contains(resolution.SourceMaterialName)
                    ? resolution with { Status = ImportMaterialResolutionStatuses.Created }
                    : resolution)
                .ToArray()
        };
    }

    private static string BuildImportFileMessage(ImportResponse response, string? filePath)
    {
        var fileName = string.IsNullOrWhiteSpace(filePath) ? "selected file" : Path.GetFileName(filePath);
        var importedRowCount = response.Parts.Count + response.RequiredPieces.Count;

        if (response.Success)
        {
            return $"Imported {importedRowCount} row(s) from '{fileName}'.";
        }

        if (importedRowCount > 0)
        {
            return $"Imported {importedRowCount} row(s) from '{fileName}' with {response.Errors.Count} error(s) and {response.Warnings.Count} warning(s).";
        }

        return GetFirstErrorMessage(response.Errors, $"Import failed for '{fileName}'.");
    }

    private static ImportSessionResponse BuildImportSessionResponse(
        string sessionId,
        ImportSessionResult result,
        ImportSessionPhase phase,
        Project? project = null,
        bool finalized = false,
        ImportPreviewSummary? previewSummary = null,
        ImportResultCounts? resultCounts = null)
    {
        var response = result.Response;
        return new ImportSessionResponse(
            response.Success,
            sessionId,
            result.ImportSource.ImportSourcePath,
            result.ImportSource,
            phase,
            finalized,
            project,
            response.Parts,
            response.Errors,
            response.Warnings,
            response.AvailableColumns,
            response.ColumnMappings,
            response.MaterialResolutions,
            null,
            BuildImportFileMessage(response, result.ImportSource.ImportSourcePath))
        {
            RequiredPieces = response.RequiredPieces,
            ResultCounts = resultCounts,
            Workbook = result.Workbook,
            SourceColumns = response.SourceColumns,
            Worksheet = response.Worksheet,
            PreviewSummary = previewSummary ?? BuildWorksheetPreviewSummary(response),
            Progress = result.Progress,
            ProgressHistory = result.ProgressHistory
        };
    }

    private static ImportResultCounts? BuildStockLengthImportResultCounts(
        Project previousProject,
        IReadOnlyList<FinalizedWorksheetImport> worksheetImports,
        Project finalizedProject)
    {
        if (finalizedProject.ProjectKind != ProjectKind.StockLength)
        {
            return null;
        }

        var selectedPositions = worksheetImports
            .Select(item => item.Selection.OriginalPosition)
            .ToHashSet();
        var sourcePieces = worksheetImports.SelectMany(item => item.Response.RequiredPieces).ToArray();
        var skippedSourceRowCount = worksheetImports.Sum(item => item.Selection.ExcludedSourceRows.Count) +
            sourcePieces.Where(piece => string.Equals(
                piece.ValidationStatus,
                ValidationStatuses.Error,
                StringComparison.Ordinal)).Sum(piece => Math.Max(1, piece.SourceReferences.Count));
        var validSourceRowCount = sourcePieces.Where(piece => !string.Equals(
            piece.ValidationStatus,
            ValidationStatuses.Error,
            StringComparison.Ordinal)).Sum(piece => Math.Max(1, piece.SourceReferences.Count));
        var sourceRowCount = validSourceRowCount + skippedSourceRowCount;
        var outputEntries = finalizedProject.State.OptimizationGroups
            .SelectMany(group => group.RequiredPieces)
            .Where(piece => !piece.IsManual && piece.SourceReferences.Any(reference =>
                selectedPositions.Contains(reference.WorksheetPosition)))
            .ToArray();
        var previousIds = previousProject.State.OptimizationGroups
            .SelectMany(group => group.RequiredPieces)
            .Where(piece => !piece.IsManual)
            .Select(piece => piece.RequiredPieceId)
            .ToHashSet(StringComparer.Ordinal);
        var updatedEntryCount = outputEntries.Count(piece => previousIds.Contains(piece.RequiredPieceId));

        return new ImportResultCounts(
            sourceRowCount,
            validSourceRowCount,
            outputEntries.Length,
            outputEntries.Sum(piece => piece.Quantity),
            outputEntries.Length - updatedEntryCount,
            updatedEntryCount,
            skippedSourceRowCount,
            worksheetImports.Count);
    }

    private static ImportResponse PrefixWorksheetRowIds(
        ImportResponse response,
        int worksheetPosition)
    {
        var rowIds = response.Parts.ToDictionary(
            part => part.RowId,
            part => $"worksheet-{worksheetPosition}-{part.RowId}",
            StringComparer.Ordinal);
        return response with
        {
            Parts = response.Parts.Select(part => part with
            {
                RowId = rowIds[part.RowId]
            }).ToArray(),
            Errors = response.Errors.Select(error => error.RowId is not null && rowIds.TryGetValue(error.RowId, out var rowId)
                ? error with { RowId = rowId }
                : error).ToArray(),
            Warnings = response.Warnings.Select(warning => warning.RowId is not null && rowIds.TryGetValue(warning.RowId, out var rowId)
                ? warning with { RowId = rowId }
                : warning).ToArray()
        };
    }

    private static string? FindConflictingMaterialResolution(
        IReadOnlyList<ImportWorksheetSelection> selections,
        IReadOnlyList<ImportNewMaterialRequest> newMaterials)
    {
        var mappedConflict = selections
            .SelectMany(selection => selection.Options?.MaterialMappings ?? [])
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping.SourceMaterialName) &&
                !string.IsNullOrWhiteSpace(mapping.TargetMaterialId))
            .GroupBy(mapping => mapping.SourceMaterialName.Trim(), StringComparer.Ordinal)
            .FirstOrDefault(group => group
                .Select(mapping => mapping.TargetMaterialId)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any())
            ?.Key;
        if (mappedConflict is not null)
        {
            return mappedConflict;
        }

        var mappedLabels = selections
            .SelectMany(selection => selection.Options?.MaterialMappings ?? [])
            .Select(mapping => mapping.SourceMaterialName.Trim())
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToHashSet(StringComparer.Ordinal);
        return newMaterials
            .Where(request => !string.IsNullOrWhiteSpace(request.SourceMaterialName))
            .GroupBy(request => request.SourceMaterialName.Trim(), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Skip(1).Any() || mappedLabels.Contains(group.Key))
            ?.Key;
    }

    private static IReadOnlyList<ImportMaterialMapping> BuildWorkbookMaterialMappings(
        IReadOnlyList<ImportWorksheetSelection> selections) =>
        selections
            .SelectMany(selection => selection.Options?.MaterialMappings ?? [])
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.SourceMaterialName))
            .GroupBy(mapping => mapping.SourceMaterialName.Trim(), StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

    private static ImportSessionResult CombineWorksheetImports(
        ImportSourceMetadata importSource,
        IReadOnlyList<FinalizedWorksheetImport> worksheetImports,
        IReadOnlyList<PartRow> combinedParts,
        IReadOnlyList<RequiredPiece> combinedRequiredPieces)
    {
        var responses = worksheetImports.Select(item => item.Response).ToArray();
        return new ImportSessionResult(
            importSource,
            new ImportResponse
            {
                Success = responses.All(response => response.Success),
                Parts = combinedParts,
                RequiredPieces = combinedRequiredPieces,
                Errors = responses.SelectMany(response => response.Errors).ToArray(),
                Warnings = responses.SelectMany(response => response.Warnings).ToArray(),
                AvailableColumns = responses.FirstOrDefault()?.AvailableColumns ?? Array.Empty<string>(),
                SourceColumns = responses.FirstOrDefault()?.SourceColumns ?? Array.Empty<ImportSourceColumn>(),
                ColumnMappings = responses.FirstOrDefault()?.ColumnMappings ?? Array.Empty<ImportFieldMappingStatus>(),
                MaterialResolutions = responses
                    .SelectMany(response => response.MaterialResolutions)
                    .GroupBy(resolution => resolution.SourceMaterialName, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToArray()
            });
    }

    private static ImportPreviewSummary? BuildWorksheetPreviewSummary(ImportResponse response)
    {
        if (response.Worksheet is null)
        {
            return null;
        }

        return new ImportPreviewSummary
        {
            Worksheets =
            [
                new ImportWorksheetPreviewSummary
                {
                    WorksheetName = response.Worksheet.WorksheetName,
                    OriginalPosition = response.Worksheet.OriginalPosition,
                    SourceRowCount = response.Parts.Sum(part => part.SourceReferences.Count) +
                        response.RequiredPieces.Sum(piece => piece.SourceReferences.Count),
                    ImportedPartCount = response.Parts.Count + response.RequiredPieces.Count,
                    IssueCount = response.Errors.Count + response.Warnings.Count
                }
            ]
        };
    }

    private static ImportPreviewSummary BuildWorkbookPreviewSummary(
        IReadOnlyList<FinalizedWorksheetImport> worksheetImports,
        IReadOnlyList<OptimizationGroup> optimizationGroups)
    {
        var worksheetSummaries = worksheetImports.Select(item => new ImportWorksheetPreviewSummary
        {
            WorksheetName = item.Selection.WorksheetName,
            OriginalPosition = item.Selection.OriginalPosition,
            SourceRowCount = item.Response.Parts.Sum(part => part.SourceReferences.Count) +
                item.Response.RequiredPieces.Sum(piece => piece.SourceReferences.Count) +
                item.Selection.ExcludedSourceRows.Count,
            ImportedPartCount = item.Response.Parts.Count + item.Response.RequiredPieces.Count,
            ExcludedRowCount = item.Selection.ExcludedSourceRows.Count,
            IssueCount = item.Response.Errors.Count + item.Response.Warnings.Count
        }).ToArray();
        var positionsByGroup = worksheetImports
            .GroupBy(item => item.Selection.OptimizationGroupId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Selection.OriginalPosition).ToHashSet(),
                StringComparer.Ordinal);
        var sourceRowsByGroup = worksheetImports
            .GroupBy(item => item.Selection.OptimizationGroupId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item =>
                    item.Response.Parts.Sum(part => part.SourceReferences.Count) +
                    item.Response.RequiredPieces.Sum(piece => piece.SourceReferences.Count) +
                    item.Selection.ExcludedSourceRows.Count),
                StringComparer.Ordinal);

        var groupSummaries = optimizationGroups
            .Where(group => positionsByGroup.ContainsKey(group.OptimizationGroupId))
            .Select(group =>
            {
                var positions = positionsByGroup[group.OptimizationGroupId];
                var combinedPartCount = group.Parts.Count(part =>
                    !part.IsManual && part.SourceReferences.Any(reference => positions.Contains(reference.WorksheetPosition))) +
                    group.RequiredPieces.Count(piece =>
                        !piece.IsManual && piece.SourceReferences.Any(reference => positions.Contains(reference.WorksheetPosition)));
                var sourceRowCount = sourceRowsByGroup[group.OptimizationGroupId];
                return new ImportOptimizationGroupPreviewSummary
                {
                    OptimizationGroupId = group.OptimizationGroupId,
                    Name = group.Name,
                    SourceRowCount = sourceRowCount,
                    CombinedPartCount = combinedPartCount,
                    MergedRowCount = Math.Max(0, sourceRowCount - combinedPartCount)
                };
            })
            .ToArray();

        return new ImportPreviewSummary
        {
            Worksheets = worksheetSummaries,
            OptimizationGroups = groupSummaries
        };
    }

    private static string GetFirstErrorCode(IReadOnlyList<ValidationError> errors, string fallbackCode) =>
        errors.Count > 0 ? errors[0].Code : fallbackCode;

    private static string GetFirstErrorMessage(IReadOnlyList<ValidationError> errors, string fallbackMessage) =>
        errors.Count > 0 ? errors[0].Message : fallbackMessage;

    private static string ResolveOperationId(string operationId) =>
        string.IsNullOrWhiteSpace(operationId)
            ? $"cut-plan-{Guid.NewGuid():N}"
            : operationId;

    private sealed class NoOpPartEditorService : IPartEditorService
    {
        public Task<ImportResponse> AddRowAsync(
            IReadOnlyList<PartRow> parts,
            PartRowUpdate update,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ImportResponse
                {
                    Success = false,
                    Parts = parts ?? Array.Empty<PartRow>(),
                    Errors = [new ValidationError("not-ready", "Part editing is not configured for this bridge instance.")],
                    Warnings = Array.Empty<ValidationWarning>()
                });

        public Task<ImportResponse> UpdateRowAsync(
            IReadOnlyList<PartRow> parts,
            PartRowUpdate update,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ImportResponse
                {
                    Success = false,
                    Parts = parts ?? Array.Empty<PartRow>(),
                    Errors = [new ValidationError("not-ready", "Part editing is not configured for this bridge instance.")],
                    Warnings = Array.Empty<ValidationWarning>()
                });

        public Task<ImportResponse> DeleteRowAsync(
            IReadOnlyList<PartRow> parts,
            string rowId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ImportResponse
                {
                    Success = false,
                    Parts = parts ?? Array.Empty<PartRow>(),
                    Errors = [new ValidationError("not-ready", "Part editing is not configured for this bridge instance.")],
                    Warnings = Array.Empty<ValidationWarning>()
                });
    }

    private sealed record ImportPreparationResult(
        bool Success,
        ImportOptions Options,
        IReadOnlySet<string> CreatedSourceMaterials,
        IReadOnlyList<Material> CreatedMaterials,
        IReadOnlyList<ValidationError> Errors);
}
