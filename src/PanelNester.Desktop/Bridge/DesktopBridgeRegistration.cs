using System.IO;
using PanelNester.Desktop;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;

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
        IMaterialLibraryLocationService? materialLibraryLocationService = null) =>
        CreateDefault(
            fileDialogService,
            materialService,
            projectService,
            importService,
            new NoOpPartEditorService(),
            nestingService,
            contentLocationAccessor,
            materialLibraryLocationService);

    public static BridgeMessageDispatcher CreateDefault(
        IFileDialogService fileDialogService,
        IMaterialService materialService,
        IProjectService projectService,
        IImportService importService,
        IPartEditorService partEditorService,
        INestingService nestingService,
        Func<WebUiContentLocation> contentLocationAccessor,
        IMaterialLibraryLocationService? materialLibraryLocationService = null) =>
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
            contentLocationAccessor,
            materialLibraryLocationService);

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
        IMaterialLibraryLocationService? materialLibraryLocationService = null)
    {
        ArgumentNullException.ThrowIfNull(fileDialogService);
        ArgumentNullException.ThrowIfNull(materialService);
        ArgumentNullException.ThrowIfNull(projectService);
        ArgumentNullException.ThrowIfNull(importService);
        ArgumentNullException.ThrowIfNull(partEditorService);
        ArgumentNullException.ThrowIfNull(nestingService);
        ArgumentNullException.ThrowIfNull(contentLocationAccessor);

        var dispatcher = new BridgeMessageDispatcher();

        dispatcher.Register<BridgeHandshakeRequest>(
            BridgeMessageTypes.BridgeHandshake,
            (request, _) =>
            {
                var contentLocation = contentLocationAccessor();
                var response = new BridgeHandshakeResponse(
                    true,
                    "PanelNester Desktop Host",
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
                var filePath = NormalizeFilePath(request.FilePath);
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    var dialogResult = await fileDialogService
                        .OpenAsync(
                            new OpenFileDialogRequest("Import PanelNester parts", ImportFileFilters),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (!dialogResult.Success || string.IsNullOrWhiteSpace(dialogResult.FilePath))
                    {
                        return ImportFileResponse.Cancelled();
                    }

                    filePath = dialogResult.FilePath;
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
            });

        dispatcher.Register<NewProjectRequest>(
            BridgeMessageTypes.NewProject,
            async (request, cancellationToken) =>
            {
                var result = await projectService
                    .NewAsync(request.Metadata, request.Settings, cancellationToken)
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
                            new OpenFileDialogRequest("Open a PanelNester project", ProjectFileFilters),
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
                            new SaveFileDialogRequest("Save PanelNester project", BuildProjectFileName(request.Project), ProjectFileFilters),
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
                                "Save PanelNester project as",
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
                var materials = await materialService.ListAsync(cancellationToken).ConfigureAwait(false);
                var libraryLocation = materialLibraryLocationService is null
                    ? null
                    : await materialLibraryLocationService.GetLocationAsync(cancellationToken).ConfigureAwait(false);
                return new ListMaterialsResponse(
                    true,
                    materials,
                    null,
                    $"Loaded {materials.Count} material(s).",
                    libraryLocation);
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
                        var location = await materialLibraryLocationService
                            .RestoreDefaultAsync(cancellationToken)
                            .ConfigureAwait(false);
                        var materials = await materialService.ListAsync(cancellationToken).ConfigureAwait(false);
                        var responseMessage = defaultFileExisted
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
                                    "Export PanelNester PDF report",
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
                        var reportData = await reportDataService
                            .BuildReportDataAsync(
                                new ReportDataRequest
                                {
                                    Project = request.Project,
                                    BatchResult = request.BatchResult
                                },
                                cancellationToken)
                            .ConfigureAwait(false);

                        await pdfReportExporter
                            .ExportAsync(reportData, filePath, cancellationToken)
                            .ConfigureAwait(false);

                        return new ExportPdfReportResponse(
                            true,
                            filePath,
                            null,
                            $"Exported PDF report to '{Path.GetFileName(filePath)}'.");
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

        return dispatcher;
    }

    private static bool IsMaterialInUse(DeleteMaterialRequest request, Material material) =>
        string.Equals(request.SelectedMaterialId, material.MaterialId, StringComparison.Ordinal) ||
        request.ImportedMaterialNames?.Any(name =>
            string.Equals(name, material.Name, StringComparison.Ordinal)) == true;

    private static readonly IReadOnlyList<FileDialogFilter> ProjectFileFilters =
    [
        new FileDialogFilter("PanelNester project files", ["pnest"]),
        new FileDialogFilter("All files", ["*.*"])
    ];

    private static readonly IReadOnlyList<FileDialogFilter> ImportFileFilters =
    [
        new FileDialogFilter("Supported import files", ["csv", "xlsx"]),
        new FileDialogFilter("CSV files", ["csv"]),
        new FileDialogFilter("Excel workbooks", ["xlsx"]),
        new FileDialogFilter("All files", ["*.*"])
    ];

    private static readonly IReadOnlyList<FileDialogFilter> PdfFileFilters =
    [
        new FileDialogFilter("PDF files", ["pdf"]),
        new FileDialogFilter("All files", ["*.*"])
    ];

    private static readonly IReadOnlyList<FileDialogFilter> MaterialLibraryFileFilters =
    [
        new FileDialogFilter("Material library files", ["json"]),
        new FileDialogFilter("All files", ["*.*"])
    ];

    private static string BuildProjectFileName(Project project)
    {
        var rawName = string.IsNullOrWhiteSpace(project.Metadata.ProjectName)
            ? "panelnester-project"
            : project.Metadata.ProjectName;
        var sanitized = string.Concat(rawName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)).Trim();
        var fileName = string.IsNullOrWhiteSpace(sanitized) ? "panelnester-project" : sanitized;

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
        var fileName = string.IsNullOrWhiteSpace(sanitized) ? "panelnester-report" : sanitized;

        return fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.pdf";
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
        if (request.NewMaterials.Count == 0)
        {
            return new ImportPreparationResult(
                true,
                requestedOptions,
                new HashSet<string>(StringComparer.Ordinal),
                Array.Empty<ValidationError>());
        }

        var createdSourceMaterials = new List<string>(request.NewMaterials.Count);
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
        }

        return errors.Count > 0
            ? new ImportPreparationResult(
                false,
                requestedOptions,
                createdSourceMaterials.ToHashSet(StringComparer.Ordinal),
                errors)
            : new ImportPreparationResult(
                true,
                requestedOptions with { MaterialMappings = existingMappings.Values.ToArray() },
                createdSourceMaterials.ToHashSet(StringComparer.Ordinal),
                Array.Empty<ValidationError>());
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

        if (response.Success)
        {
            return $"Imported {response.Parts.Count} row(s) from '{fileName}'.";
        }

        if (response.Parts.Count > 0)
        {
            return $"Imported {response.Parts.Count} row(s) from '{fileName}' with {response.Errors.Count} error(s) and {response.Warnings.Count} warning(s).";
        }

        return GetFirstErrorMessage(response.Errors, $"Import failed for '{fileName}'.");
    }

    private static string GetFirstErrorCode(IReadOnlyList<ValidationError> errors, string fallbackCode) =>
        errors.Count > 0 ? errors[0].Code : fallbackCode;

    private static string GetFirstErrorMessage(IReadOnlyList<ValidationError> errors, string fallbackMessage) =>
        errors.Count > 0 ? errors[0].Message : fallbackMessage;

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
        IReadOnlyList<ValidationError> Errors);
}
