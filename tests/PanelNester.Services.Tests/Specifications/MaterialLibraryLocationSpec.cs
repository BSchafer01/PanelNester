using System.Text.Json;
using PanelNester.Domain.Models;
using PanelNester.Services.Materials;

namespace PanelNester.Services.Tests.Specifications;

internal static class MaterialLibraryLocationSpec
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    internal static string GetCanonicalDefaultLibraryPath(string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        return Path.GetFullPath(Path.Combine(workspacePath, "PanelNester", "materials.json"));
    }

    internal static string GetLocationStoreFilePath(string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        return Path.GetFullPath(Path.Combine(workspacePath, "PanelNester", "material-library-location.json"));
    }

    internal static JsonMaterialRepositoryOptions CreateOptions(string workspacePath) =>
        new()
        {
            DefaultFilePath = GetCanonicalDefaultLibraryPath(workspacePath),
            LocationStoreFilePath = GetLocationStoreFilePath(workspacePath)
        };

    internal static Material CreateMaterial(string materialId, string name) =>
        new()
        {
            MaterialId = materialId,
            Name = name,
            SheetLength = 96m,
            SheetWidth = 48m,
            AllowRotation = true,
            DefaultSpacing = 0.125m,
            DefaultEdgeMargin = 0.5m
        };

    internal static async Task WriteRawLibraryFileAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(content);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(filePath, content, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<string?> TryLoadStoredActiveLibraryPathAsync(
        string settingsFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);

        if (!File.Exists(settingsFilePath))
        {
            return null;
        }

        await using var stream = new FileStream(
            settingsFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        var document = await JsonSerializer.DeserializeAsync<MaterialLibraryLocationSettings>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(document?.ActiveFilePath)
            ? null
            : Path.GetFullPath(document.ActiveFilePath);
    }

    private sealed class MaterialLibraryLocationSettings
    {
        public string? ActiveFilePath { get; init; }
    }
}
