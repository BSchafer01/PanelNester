using System.Text.Json.Serialization;

namespace PanelNester.Domain.Models;

public sealed record MaterialLibraryLocation
{
    [JsonPropertyName("currentPath")]
    public string ActiveFilePath { get; init; } = string.Empty;

    [JsonPropertyName("defaultPath")]
    public string DefaultFilePath { get; init; } = string.Empty;

    [JsonPropertyName("usingDefaultLocation")]
    public bool UsesDefaultLocation { get; init; }
}
