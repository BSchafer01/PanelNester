using System.IO;
using System.Text.Json;

namespace PanelNester.Desktop;

public sealed class DesktopAppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public DesktopAppSettingsStore(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public DesktopAppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new DesktopAppSettings();
            }

            var json = File.ReadAllText(_filePath);
            return string.IsNullOrWhiteSpace(json)
                ? new DesktopAppSettings()
                : JsonSerializer.Deserialize<DesktopAppSettings>(json, SerializerOptions) ?? new DesktopAppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new DesktopAppSettings();
        }
    }

    public void Save(DesktopAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        EnsureDirectory();
        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
