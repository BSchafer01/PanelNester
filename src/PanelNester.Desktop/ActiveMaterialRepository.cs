using System.IO;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services.Materials;

namespace PanelNester.Desktop;

public interface IMaterialLibraryPathManager
{
    MaterialLibraryLocationInfo GetLocationInfo();

    Task<MaterialLibraryReloadResult> SetActiveFilePathAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<MaterialLibraryReloadResult> RestoreDefaultAsync(
        CancellationToken cancellationToken = default);
}

public sealed record MaterialLibraryLocationInfo(
    string ActiveFilePath,
    string DefaultFilePath,
    bool IsDefaultPath);

public sealed record MaterialLibraryReloadResult(
    IReadOnlyList<Material> Materials,
    MaterialLibraryLocationInfo LocationInfo);

public sealed class ActiveMaterialRepository : IMaterialRepository, IMaterialLibraryPathManager
{
    private readonly DesktopAppSettingsStore _settingsStore;
    private readonly string _defaultFilePath;
    private readonly SemaphoreSlim _pathGate = new(1, 1);
    private readonly object _repositorySync = new();
    private JsonMaterialRepository _repository;
    private string _activeFilePath;

    public ActiveMaterialRepository(
        DesktopAppSettingsStore settingsStore,
        string defaultFilePath)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _defaultFilePath = NormalizeFilePath(defaultFilePath);

        var settings = _settingsStore.Load();
        _activeFilePath = ResolveInitialFilePath(settings);
        _repository = CreateRepository(_activeFilePath);
    }

    public MaterialLibraryLocationInfo GetLocationInfo()
    {
        lock (_repositorySync)
        {
            return CreateLocationInfo(_activeFilePath);
        }
    }

    public Task<IReadOnlyList<Material>> GetAllAsync(CancellationToken cancellationToken = default) =>
        GetCurrentRepository().GetAllAsync(cancellationToken);

    public Task<Material?> GetByIdAsync(string materialId, CancellationToken cancellationToken = default) =>
        GetCurrentRepository().GetByIdAsync(materialId, cancellationToken);

    public Task<Material> CreateAsync(Material material, CancellationToken cancellationToken = default) =>
        GetCurrentRepository().CreateAsync(material, cancellationToken);

    public Task<Material> UpdateAsync(Material material, CancellationToken cancellationToken = default) =>
        GetCurrentRepository().UpdateAsync(material, cancellationToken);

    public Task DeleteAsync(string materialId, CancellationToken cancellationToken = default) =>
        GetCurrentRepository().DeleteAsync(materialId, cancellationToken);

    public async Task<MaterialLibraryReloadResult> SetActiveFilePathAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizeFilePath(filePath);
        var repository = CreateRepository(normalizedPath);
        var materials = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        await _pathGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var currentSettings = _settingsStore.Load();
            _settingsStore.Save(new DesktopAppSettings
            {
                ActiveMaterialLibraryPath = PathsMatch(normalizedPath, _defaultFilePath)
                    ? null
                    : normalizedPath,
                CompanyLogoPath = currentSettings.CompanyLogoPath,
                CompanyName = currentSettings.CompanyName
            });

            lock (_repositorySync)
            {
                _activeFilePath = normalizedPath;
                _repository = repository;
            }

            return new MaterialLibraryReloadResult(materials, CreateLocationInfo(normalizedPath));
        }
        finally
        {
            _pathGate.Release();
        }
    }

    public Task<MaterialLibraryReloadResult> RestoreDefaultAsync(CancellationToken cancellationToken = default) =>
        SetActiveFilePathAsync(_defaultFilePath, cancellationToken);

    private JsonMaterialRepository GetCurrentRepository()
    {
        lock (_repositorySync)
        {
            return _repository;
        }
    }

    private MaterialLibraryLocationInfo CreateLocationInfo(string activeFilePath) =>
        new(
            activeFilePath,
            _defaultFilePath,
            PathsMatch(activeFilePath, _defaultFilePath));

    private JsonMaterialRepository CreateRepository(string filePath) =>
        new(filePath);

    private string ResolveInitialFilePath(DesktopAppSettings settings)
    {
        var configuredPath = settings.ActiveMaterialLibraryPath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return _defaultFilePath;
        }

        try
        {
            return NormalizeFilePath(configuredPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return _defaultFilePath;
        }
    }

    private static string NormalizeFilePath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Path.GetFullPath(filePath.Trim());
    }

    private static bool PathsMatch(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
