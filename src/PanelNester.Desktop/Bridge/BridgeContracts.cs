using System.Text.Json;
using PanelNester.Domain.Models;

namespace PanelNester.Desktop.Bridge;

public static class BridgeMessageTypes
{
    public const string BridgeHandshake = "bridge-handshake";
    public const string OpenFileDialog = "open-file-dialog";
    public const string ImportCsv = "import-csv";
    public const string ImportFile = "import-file";
    public const string UpdatePartRow = "update-part-row";
    public const string DeletePartRow = "delete-part-row";
    public const string AddPartRow = "add-part-row";
    public const string RunNesting = "run-nesting";
    public const string RunBatchNesting = "run-batch-nesting";
    public const string ListMaterials = "list-materials";
    public const string GetMaterial = "get-material";
    public const string CreateMaterial = "create-material";
    public const string UpdateMaterial = "update-material";
    public const string DeleteMaterial = "delete-material";
    public const string NewProject = "new-project";
    public const string OpenProject = "open-project";
    public const string SaveProject = "save-project";
    public const string SaveProjectAs = "save-project-as";
    public const string GetProjectMetadata = "get-project-metadata";
    public const string UpdateProjectMetadata = "update-project-metadata";
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
            "project-not-found" =>
                "The selected project file could not be found.",
            "project-corrupt" =>
                "The selected project file could not be opened.",
            "project-unsupported-version" =>
                "This project file was created by a newer version of PanelNester.",
            "project-create-failed" =>
                "The project could not be created.",
            "project-save-failed" =>
                "The project could not be saved. Please try again.",
            "project-update-failed" =>
                "The project details could not be updated.",
            "report-settings-update-failed" =>
                "The report settings could not be updated.",
            "report-export-failed" =>
                "The PDF report could not be exported. Please try again.",
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
            message);
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
    string? DefaultExtension = null);

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

public sealed record ListMaterialsResponse(bool Success, IReadOnlyList<Material> Materials, BridgeError? Error, string? Message)
{
    public static ListMaterialsResponse Failure(string code, string message, string? userMessage = null)
    {
        var failure = BridgeFailure.Create(code, message, userMessage);
        return new(false, Array.Empty<Material>(), failure.Error, failure.ResponseMessage);
    }
}

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

public sealed record NewProjectRequest(ProjectMetadata? Metadata = null, ProjectSettings? Settings = null);

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
    string? SuggestedFileName = null);

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
