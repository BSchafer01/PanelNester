namespace PanelNester.Services.Materials;

public sealed class JsonMaterialRepositoryOptions
{
    public string? DefaultFilePath { get; init; }

    public string? LocationStoreFilePath { get; init; }

    public MaterialValidationService? ValidationService { get; init; }
}
