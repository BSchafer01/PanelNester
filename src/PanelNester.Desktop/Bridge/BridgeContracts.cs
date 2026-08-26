using System.Text.Json;
using PanelNester.Domain.Models;

namespace PanelNester.Desktop.Bridge;

public static class BridgeMessageTypes
{
    public const string BridgeHandshake = "bridge-handshake";
    public const string BridgeUiReady = "bridge-ui-ready";
    public const string OpenFileDialog = "open-file-dialog";
    public const string ImportCsv = "import-csv";
    public const string ImportFile = "import-file";
    public const string BeginImportSession = "begin-import-session";
    public const string PreviewImportSession = "preview-import-session";
    public const string FinalizeImportSession = "finalize-import-session";
    public const string CancelImportSession = "cancel-import-session";
    public const string GetImportSessionProgress = "get-import-session-progress";
    public const string UpdatePartRow = "update-part-row";
    public const string DeletePartRow = "delete-part-row";
    public const string AddPartRow = "add-part-row";
    public const string RunNesting = "run-nesting";
    public const string RunBatchNesting = "run-batch-nesting";
    public const string ListMaterials = "list-materials";
    public const string GetStiffenerTakeoff = "get-stiffener-takeoff";
    public const string GetExtrusionLayout = "get-extrusion-layout";
    public const string UpdateExtrusionLayout = "update-extrusion-layout";
    public const string GetExtrusionReport = "get-extrusion-report";
    public const string ExportExcelReport = "export-excel-report";
    public const string ExportStiffenerPdfReport = "export-stiffener-pdf-report";
    public const string ExportExtrusionPdfReport = "export-extrusion-pdf-report";
    public const string ExportExtrusionExcelReport = "export-extrusion-excel-report";
    public const string GetMaterial = "get-material";
    public const string CreateMaterial = "create-material";
    public const string UpdateMaterial = "update-material";
    public const string DeleteMaterial = "delete-material";
    public const string ChooseMaterialLibraryLocation = "choose-material-library-location";
    public const string RestoreDefaultMaterialLibraryLocation = "restore-default-material-library-location";
    public const string NewProject = "new-project";
    public const string OpenProject = "open-project";
    public const string SaveProject = "save-project";
    public const string SaveProjectAs = "save-project-as";
    public const string GetProjectMetadata = "get-project-metadata";
    public const string UpdateProjectMetadata = "update-project-metadata";
    public const string ChangeProjectKind = "change-project-kind";
    public const string UpdateOptimizationGroups = "update-optimization-groups";
    public const string UpdateRequiredPieces = "update-required-pieces";
    public const string GenerateSelectedCutPlan = "generate-selected-cut-plan";
    public const string GetDesktopAppSettings = "get-desktop-app-settings";
    public const string UpdateDesktopAppSettings = "update-desktop-app-settings";
    public const string UpdateReportSettings = "update-report-settings";
    public const string ExportPdfReport = "export-pdf-report";

    public static string ToResponseType(string requestType) => $"{requestType}-response";
}

public sealed record BridgeMessageEnvelope(string Type, string? RequestId, JsonElement Payload);

public sealed record BridgeError(string Code, string Message, string? UserMessage = null)
{
    public static BridgeError Create(string code, string message, string? userMessage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return new BridgeError(
            code,
            message,
            ResolveUserMessage(code, message, userMessage));
    }

    private static string? ResolveUserMessage(string code, string message, string? userMessage)
    {
        if (!string.IsNullOrWhiteSpace(userMessage))
        {
            return userMessage;
        }

        return code switch
        {
            "cancelled" => null,
            "invalid-message" or "invalid-payload" =>
                "The desktop host could not understand that request.",
            "unsupported-message" =>
                "This action is not available in the current desktop host.",
            "host-error" =>
                "The desktop host ran into an unexpected problem. Please try again.",
            "material-id-required" =>
                "Choose a material and try again.",
            "material-library-path-required" =>
                "Choose a material library file and try again.",
            "material-library-invalid-path" =>
                "Choose a valid .json file and try again.",
            "material-library-load-failed" =>
                "The selected material library could not be opened.",
            "project-not-found" =>
                "The selected project file could not be found.",
            "project-corrupt" =>
                "The selected project file could not be opened.",
            "project-unsupported-version" =>
                "This project file was created by a newer version of OptiFab.",
            "project-create-failed" =>
                "The project could not be created.",
            "project-save-failed" =>
                "The project could not be saved. Please try again.",
            "project-update-failed" =>
                "The project details could not be updated.",
            "project-kind-change-not-empty" =>
                "Remove all sheet parts or Required Pieces before changing Project Kind.",
            "project-kind-invalid" =>
                "Choose either Sheet Project or Stock-Length Project.",
            "optimization-group-name-required" =>
                "Enter an Optimization Group name.",
            "optimization-group-name-duplicate" =>
                "Optimization Group names must be unique within the project.",
            "optimization-group-not-found" =>
                "The Optimization Group could not be found.",
            "optimization-group-not-empty" =>
                "Reassign or explicitly remove the Optimization Group's owned content first.",
            "optimization-group-last-group" =>
                "A project must keep at least one Optimization Group.",
            "optimization-group-part-not-manual" =>
                "Imported parts move with their Worksheet. Move only manual parts individually.",
            "stock-length-required" or "stock-length-invalid" =>
                "Enter a positive Stock Length in inches.",
            "required-piece-quantity-invalid" =>
                "Quantity must be a positive whole number.",
            "required-piece-length-invalid" =>
                "Length must be a positive decimal, fraction, or mixed-number inch measurement.",
            "required-piece-profile-required" =>
                "Profile Number is required.",
            "required-piece-not-found" =>
                "The Required Piece could not be found.",
            "report-settings-update-failed" =>
                "The report settings could not be updated.",
            "desktop-settings-update-failed" =>
                "The application settings could not be updated.",
            "report-export-failed" =>
                "The PDF report could not be exported. Please try again.",
            "report-excel-export-failed" =>
                "The Excel report could not be exported. Please try again.",
            "stiffener-takeoff-failed" =>
                "The stiffener takeoff could not be calculated.",
            "stiffener-report-disabled" =>
                "Enable stiffener takeoff in the project settings first.",
            "stiffener-report-export-failed" =>
                "The stiffener PDF report could not be exported. Please try again.",
            "extrusion-layout-failed" =>
                "The extrusion layout could not be prepared.",
            "extrusion-report-failed" =>
                "The extrusion report could not be calculated.",
            "extrusion-report-export-failed" =>
                "The extrusion report could not be exported. Please try again.",
            "material-library-location-update-failed" =>
                "The material library location could not be changed.",
            "material-library-restore-failed" =>
                "The default material library could not be restored.",
            "invalid-output-path" =>
                "Choose a different save location and try again.",
            _ => string.IsNullOrWhiteSpace(message)
                ? "The desktop host could not complete the request."
                : message
        };
    }
}

internal readonly record struct BridgeFailure(BridgeError Error, string ResponseMessage)
{
    public static BridgeFailure Create(string code, string message, string? userMessage = null)
    {
        var error = BridgeError.Create(code, message, userMessage);
        return new BridgeFailure(error, error.UserMessage ?? message);
    }
}

public sealed record BridgeOperationResponse(bool Success, string Message, BridgeError? Error = null)
{
    public static BridgeOperationResponse Fault(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, failure.ResponseMessage, failure.Error);
    }

    public static BridgeOperationResponse NotReady(string message) =>
        Fault("not-ready", message);
}

public sealed record BridgeHandshakeRequest(
    string Surface,
    string Version,
    IReadOnlyList<string> RequestedCapabilities);

public sealed record BridgeHandshakeResponse(
    bool Success,
    string HostName,
    string HostVersion,
    string BridgeMode,
    IReadOnlyList<string> Capabilities,
    string? Message);

public sealed record BridgeUiReadyRequest();

public sealed record OpenFileDialogRequest(string? Title, IReadOnlyList<FileDialogFilter>? Filters);

public sealed record FileDialogFilter(string Name, IReadOnlyList<string> Extensions);

public sealed record OpenFileDialogResponse(bool Success, string? FilePath, BridgeError? Error, string? Message)
{
    public static OpenFileDialogResponse NotReady(string message) =>
        CreateFailure("not-ready", message);

    public static OpenFileDialogResponse Cancelled() =>
        CreateFailure("cancelled", "File selection was cancelled.");

    private static OpenFileDialogResponse CreateFailure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record ImportFileRequest
{
    public string? FilePath { get; init; }

    public ImportOptions? Options { get; init; }

    public IReadOnlyList<ImportNewMaterialRequest> NewMaterials { get; init; } = Array.Empty<ImportNewMaterialRequest>();
}

public sealed record ImportNewMaterialRequest
{
    public string SourceMaterialName { get; init; } = string.Empty;

    public Material? Material { get; init; }
}

public sealed record ImportFileResponse(
    bool Success,
    string? FilePath,
    IReadOnlyList<PartRow> Parts,
    IReadOnlyList<ValidationError> Errors,
    IReadOnlyList<ValidationWarning> Warnings,
    IReadOnlyList<string> AvailableColumns,
    IReadOnlyList<ImportFieldMappingStatus> ColumnMappings,
    IReadOnlyList<ImportMaterialResolution> MaterialResolutions,
    BridgeError? Error,
    string? Message)
{
    public IReadOnlyList<ImportSourceColumn> SourceColumns { get; init; } = Array.Empty<ImportSourceColumn>();

    public ImportWorksheetDescriptor? Worksheet { get; init; }

    public static ImportFileResponse Cancelled() =>
        Failure(null, "cancelled", "File selection was cancelled.");

    public static ImportFileResponse Failure(string? filePath, string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(
            false,
            filePath,
            Array.Empty<PartRow>(),
            Array.Empty<ValidationError>(),
            Array.Empty<ValidationWarning>(),
            Array.Empty<string>(),
            Array.Empty<ImportFieldMappingStatus>(),
            Array.Empty<ImportMaterialResolution>(),
            failure.Error,
            failure.ResponseMessage);
    }

    public static ImportFileResponse FromImportResponse(ImportResponse response, string? filePath, string? message = null) =>
        new(
            response.Success,
            filePath,
            response.Parts,
            response.Errors,
            response.Warnings,
            response.AvailableColumns,
            response.ColumnMappings,
            response.MaterialResolutions,
            null,
            message)
        {
            SourceColumns = response.SourceColumns,
            Worksheet = response.Worksheet
        };
}

public sealed record AddPartRowRequest
{
    public IReadOnlyList<PartRow>? Parts { get; init; }

    public PartRowUpdate? Part { get; init; }
}

public sealed record UpdatePartRowRequest
{
    public IReadOnlyList<PartRow>? Parts { get; init; }

    public PartRowUpdate? Part { get; init; }
}

public sealed record DeletePartRowRequest
{
    public IReadOnlyList<PartRow>? Parts { get; init; }

    public string? RowId { get; init; }
}

public sealed record SaveFileDialogRequest(
    string? Title,
    string? FileName,
    IReadOnlyList<FileDialogFilter>? Filters,
    string? DefaultExtension = null,
    bool OverwritePrompt = true);

public sealed record SaveFileDialogResponse(bool Success, string? FilePath, BridgeError? Error, string? Message)
{
    public static SaveFileDialogResponse Cancelled() =>
        CreateFailure("cancelled", "File save was cancelled.");

    private static SaveFileDialogResponse CreateFailure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record ListMaterialsRequest();

public sealed record ListMaterialsResponse(
    bool Success,
    IReadOnlyList<Material> Materials,
    BridgeError? Error,
    string? Message,
    MaterialLibraryLocation? LibraryLocation = null)
{
    public static ListMaterialsResponse Failure(
        string code,
        string message,
        string? userMessage = null,
        MaterialLibraryLocation? libraryLocation = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, Array.Empty<Material>(), failure.Error, failure.ResponseMessage, libraryLocation);
    }
}

public sealed record BeginImportSessionRequest
{
    public string SessionId { get; init; } = string.Empty;

    public string? ImportSourcePath { get; init; }

    public ProjectKind ProjectKind { get; init; } = ProjectKind.Sheet;
}

public sealed record PreviewImportSessionRequest
{
    public string SessionId { get; init; } = string.Empty;

    public ImportOptions? Options { get; init; }

    public IReadOnlyList<ImportNewMaterialRequest> NewMaterials { get; init; } =
        Array.Empty<ImportNewMaterialRequest>();

    public string? WorksheetName { get; init; }

    public string? HeadingRange { get; init; }
}

public sealed record ImportWorksheetSelection
{
    public string WorksheetName { get; init; } = string.Empty;

    public int OriginalPosition { get; init; }

    public ImportOptions? Options { get; init; }

    public string OptimizationGroupId { get; init; } = string.Empty;

    public string OptimizationGroupName { get; init; } = string.Empty;

    public string HeadingRange { get; init; } = string.Empty;

    public IReadOnlyList<ExcludedSourceRow> ExcludedSourceRows { get; init; } =
        Array.Empty<ExcludedSourceRow>();

    public IReadOnlyList<string> IgnoredMaterialNames { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<PartOverride> PartOverrides { get; init; } =
        Array.Empty<PartOverride>();
}

public sealed record FinalizeImportSessionRequest
{
    public string SessionId { get; init; } = string.Empty;

    public ImportOptions? Options { get; init; }

    public IReadOnlyList<ImportNewMaterialRequest> NewMaterials { get; init; } = Array.Empty<ImportNewMaterialRequest>();

    public Project? Project { get; init; }

    public bool ReplaceExistingImportSource { get; init; }

    public string? TargetOptimizationGroupId { get; init; }

    public IReadOnlyList<ImportWorksheetSelection> Worksheets { get; init; } =
        Array.Empty<ImportWorksheetSelection>();
}

public sealed record CancelImportSessionRequest
{
    public string SessionId { get; init; } = string.Empty;
}

public sealed record GetImportSessionProgressRequest
{
    public string SessionId { get; init; } = string.Empty;
}

public enum ImportSessionPhase
{
    Opening,
    Reading,
    Validating,
    Finalizing,
    Finalized,
    Cancelled,
    Failed
}

public sealed record ImportSessionResponse(
    bool Success,
    string SessionId,
    string? ImportSourcePath,
    ImportSourceMetadata? ImportSource,
    ImportSessionPhase Phase,
    bool Finalized,
    Project? Project,
    IReadOnlyList<PartRow> Parts,
    IReadOnlyList<ValidationError> Errors,
    IReadOnlyList<ValidationWarning> Warnings,
    IReadOnlyList<string> AvailableColumns,
    IReadOnlyList<ImportFieldMappingStatus> ColumnMappings,
    IReadOnlyList<ImportMaterialResolution> MaterialResolutions,
    BridgeError? Error,
    string? Message)
{
    public IReadOnlyList<RequiredPiece> RequiredPieces { get; init; } = Array.Empty<RequiredPiece>();

    public WorkbookDiscovery? Workbook { get; init; }

    public IReadOnlyList<ImportSourceColumn> SourceColumns { get; init; } = Array.Empty<ImportSourceColumn>();

    public ImportWorksheetDescriptor? Worksheet { get; init; }

    public ImportPreviewSummary? PreviewSummary { get; init; }

    public WorkbookImportProgress? Progress { get; init; }

    public IReadOnlyList<WorkbookImportProgress> ProgressHistory { get; init; } =
        Array.Empty<WorkbookImportProgress>();

    public static ImportSessionResponse Failure(
        string sessionId,
        string? importSourcePath,
        ImportSessionPhase phase,
        string code,
        string message,
        string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(
            false,
            sessionId,
            importSourcePath,
            null,
            phase,
            false,
            null,
            Array.Empty<PartRow>(),
            Array.Empty<ValidationError>(),
            Array.Empty<ValidationWarning>(),
            Array.Empty<string>(),
            Array.Empty<ImportFieldMappingStatus>(),
            Array.Empty<ImportMaterialResolution>(),
            failure.Error,
            failure.ResponseMessage);
    }
}

public sealed record CancelImportSessionResponse(
    bool Success,
    string SessionId,
    bool Released,
    BridgeError? Error,
    string? Message);

public sealed record GetImportSessionProgressResponse(
    bool Success,
    string SessionId,
    WorkbookImportProgress? Progress,
    IReadOnlyList<WorkbookImportProgress> History,
    BridgeError? Error,
    string? Message);

public sealed record GetMaterialRequest(string MaterialId);

public sealed record GetMaterialResponse(bool Success, Material? Material, BridgeError? Error, string? Message)
{
    public static GetMaterialResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record CreateMaterialRequest(Material Material);

public sealed record CreateMaterialResponse(bool Success, Material? Material, BridgeError? Error, string? Message)
{
    public static CreateMaterialResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record UpdateMaterialRequest(Material Material);

public sealed record UpdateMaterialResponse(bool Success, Material? Material, BridgeError? Error, string? Message)
{
    public static UpdateMaterialResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record DeleteMaterialRequest(
    string MaterialId,
    string? SelectedMaterialId = null,
    IReadOnlyList<string>? ImportedMaterialNames = null);

public sealed record DeleteMaterialResponse(bool Success, string MaterialId, BridgeError? Error, string? Message)
{
    public static DeleteMaterialResponse Failure(string materialId, string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, materialId, failure.Error, failure.ResponseMessage);
    }
}

public sealed record ChooseMaterialLibraryLocationRequest();

public sealed record ChooseMaterialLibraryLocationResponse(
    bool Success,
    IReadOnlyList<Material> Materials,
    MaterialLibraryLocation? LibraryLocation,
    BridgeError? Error,
    string? Message)
{
    public static ChooseMaterialLibraryLocationResponse Cancelled() =>
        Failure("cancelled", "Material library location selection was cancelled.");

    public static ChooseMaterialLibraryLocationResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, Array.Empty<Material>(), null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record RestoreDefaultMaterialLibraryLocationRequest();

public sealed record RestoreDefaultMaterialLibraryLocationResponse(
    bool Success,
    IReadOnlyList<Material> Materials,
    MaterialLibraryLocation? LibraryLocation,
    BridgeError? Error,
    string? Message)
{
    public static RestoreDefaultMaterialLibraryLocationResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, Array.Empty<Material>(), null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record NewProjectRequest(
    ProjectMetadata? Metadata = null,
    ProjectSettings? Settings = null,
    ProjectKind ProjectKind = ProjectKind.Sheet);

public sealed record NewProjectResponse(bool Success, Project? Project, BridgeError? Error, string? Message)
{
    public static NewProjectResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record OpenProjectRequest(string? FilePath = null);

public sealed record OpenProjectResponse(bool Success, Project? Project, string? FilePath, BridgeError? Error, string? Message)
{
    public static OpenProjectResponse Failure(string? filePath, string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, filePath, failure.Error, failure.ResponseMessage);
    }

    public static OpenProjectResponse Cancelled() =>
        Failure(null, "cancelled", "Project selection was cancelled.");
}

public sealed record SaveProjectRequest(Project Project, string? FilePath = null);

public sealed record SaveProjectResponse(bool Success, Project? Project, string? FilePath, BridgeError? Error, string? Message)
{
    public static SaveProjectResponse Failure(string? filePath, string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, filePath, failure.Error, failure.ResponseMessage);
    }

    public static SaveProjectResponse Cancelled() =>
        Failure(null, "cancelled", "Project save was cancelled.");
}

public sealed record SaveProjectAsRequest(Project Project, string? FilePath = null, string? SuggestedFileName = null);

public sealed record SaveProjectAsResponse(bool Success, Project? Project, string? FilePath, BridgeError? Error, string? Message)
{
    public static SaveProjectAsResponse Failure(string? filePath, string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, filePath, failure.Error, failure.ResponseMessage);
    }

    public static SaveProjectAsResponse Cancelled() =>
        Failure(null, "cancelled", "Project save was cancelled.");
}

public sealed record GetProjectMetadataRequest(Project Project);

public sealed record DesktopAppSettingsPayload(string? CompanyLogoPath, string? CompanyName);

public sealed record GetDesktopAppSettingsRequest();

public sealed record GetDesktopAppSettingsResponse(
    bool Success,
    DesktopAppSettingsPayload? Settings,
    BridgeError? Error,
    string? Message)
{
    public static GetDesktopAppSettingsResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record UpdateDesktopAppSettingsRequest(DesktopAppSettingsPayload Settings);

public sealed record UpdateDesktopAppSettingsResponse(
    bool Success,
    DesktopAppSettingsPayload? Settings,
    BridgeError? Error,
    string? Message)
{
    public static UpdateDesktopAppSettingsResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record GetStiffenerTakeoffRequest(Project Project);

public sealed record GetStiffenerTakeoffResponse(
    bool Success,
    StiffenerTakeoffReportData? Report,
    BridgeError? Error,
    string? Message)
{
    public static GetStiffenerTakeoffResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record GetExtrusionLayoutRequest(Project Project);

public sealed record GetExtrusionLayoutResponse(
    bool Success,
    ExtrusionLayoutState? Layout,
    BridgeError? Error,
    string? Message)
{
    public static GetExtrusionLayoutResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record UpdateExtrusionLayoutRequest(Project Project, ExtrusionLayoutState Layout);

public sealed record UpdateExtrusionLayoutResponse(
    bool Success,
    Project? Project,
    ExtrusionLayoutState? Layout,
    BridgeError? Error,
    string? Message)
{
    public static UpdateExtrusionLayoutResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record GetExtrusionReportRequest(Project Project);

public sealed record GetExtrusionReportResponse(
    bool Success,
    ExtrusionReportData? Report,
    BridgeError? Error,
    string? Message)
{
    public static GetExtrusionReportResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record GetProjectMetadataResponse(
    bool Success,
    ProjectMetadata? Metadata,
    ProjectSettings? Settings,
    BridgeError? Error,
    string? Message)
{
    public static GetProjectMetadataResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record UpdateProjectMetadataRequest(Project Project, ProjectMetadata Metadata, ProjectSettings? Settings = null);

public sealed record UpdateProjectMetadataResponse(
    bool Success,
    Project? Project,
    ProjectMetadata? Metadata,
    ProjectSettings? Settings,
    BridgeError? Error,
    string? Message)
{
    public static UpdateProjectMetadataResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, null, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record ChangeProjectKindRequest(Project Project, ProjectKind ProjectKind);

public sealed record ChangeProjectKindResponse(
    bool Success,
    Project? Project,
    BridgeError? Error,
    string? Message)
{
    public static ChangeProjectKindResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record UpdateOptimizationGroupsRequest(Project Project, OptimizationGroupChange Change);

public sealed record UpdateOptimizationGroupsResponse(
    bool Success,
    Project? Project,
    BridgeError? Error,
    string? Message)
{
    public static UpdateOptimizationGroupsResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record UpdateRequiredPiecesRequest(Project Project, RequiredPieceChange Change);

public sealed record UpdateRequiredPiecesResponse(
    bool Success,
    Project? Project,
    BridgeError? Error,
    string? Message)
{
    public static UpdateRequiredPiecesResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record GenerateSelectedCutPlanRequest(Project Project, string OptimizationGroupId);

public sealed record GenerateSelectedCutPlanResponse(
    bool Success,
    Project? Project,
    StockLengthOptimizationResult? Result,
    BridgeError? Error,
    string? Message)
{
    public static GenerateSelectedCutPlanResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record UpdateReportSettingsRequest(Project Project, ReportSettings ReportSettings);

public sealed record UpdateReportSettingsResponse(
    bool Success,
    Project? Project,
    ReportSettings? ReportSettings,
    BridgeError? Error,
    string? Message)
{
    public static UpdateReportSettingsResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, null, null, failure.Error, failure.ResponseMessage);
    }
}

public sealed record ExportPdfReportRequest(
    Project Project,
    BatchNestResponse? BatchResult = null,
    string? FilePath = null,
    string? SuggestedFileName = null,
    string? CompanyLogoPath = null);

public sealed record ExportPdfReportResponse(bool Success, string? FilePath, BridgeError? Error, string? Message)
{
    public static ExportPdfReportResponse Failure(string? filePath, string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, filePath, failure.Error, failure.ResponseMessage);
    }

    public static ExportPdfReportResponse Cancelled() =>
        Failure(null, "cancelled", "PDF export was cancelled.");
}

public sealed record ExportExcelReportRequest(
    Project Project,
    BatchNestResponse? BatchResult = null,
    string? FilePath = null,
    string? SuggestedFileName = null);

public sealed record ExportExcelReportResponse(bool Success, string? FilePath, BridgeError? Error, string? Message)
{
    public static ExportExcelReportResponse Failure(string? filePath, string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, filePath, failure.Error, failure.ResponseMessage);
    }

    public static ExportExcelReportResponse Cancelled() =>
        Failure(null, "cancelled", "Excel export was cancelled.");
}

public sealed record ExportStiffenerPdfReportRequest(
    Project Project,
    string? FilePath = null,
    string? SuggestedFileName = null,
    string? CompanyLogoPath = null);

public sealed record ExportStiffenerPdfReportResponse(bool Success, string? FilePath, BridgeError? Error, string? Message)
{
    public static ExportStiffenerPdfReportResponse Failure(string? filePath, string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, filePath, failure.Error, failure.ResponseMessage);
    }

    public static ExportStiffenerPdfReportResponse Cancelled() =>
        Failure(null, "cancelled", "PDF export was cancelled.");
}

public sealed record ExportExtrusionPdfReportRequest(
    Project Project,
    string? FilePath = null,
    string? SuggestedFileName = null,
    string? CompanyLogoPath = null);

public sealed record ExportExtrusionPdfReportResponse(bool Success, string? FilePath, BridgeError? Error, string? Message)
{
    public static ExportExtrusionPdfReportResponse Failure(string? filePath, string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, filePath, failure.Error, failure.ResponseMessage);
    }

    public static ExportExtrusionPdfReportResponse Cancelled() =>
        Failure(null, "cancelled", "PDF export was cancelled.");
}

public sealed record ExportExtrusionExcelReportRequest(
    Project Project,
    string? FilePath = null,
    string? SuggestedFileName = null);

public sealed record ExportExtrusionExcelReportResponse(bool Success, string? FilePath, BridgeError? Error, string? Message)
{
    public static ExportExtrusionExcelReportResponse Failure(string? filePath, string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, filePath, failure.Error, failure.ResponseMessage);
    }

    public static ExportExtrusionExcelReportResponse Cancelled() =>
        Failure(null, "cancelled", "Excel export was cancelled.");
}
