using System.Text.Json;
using PanelNester.Domain;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Materials;

public sealed class JsonMaterialRepository : IMaterialRepository, IMaterialLibraryLocationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _defaultFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly MaterialLibraryLocationStore? _locationStore;
    private readonly MaterialValidationService _validationService;
    private string _activeFilePath;
    private bool _locationRequiresSynchronization;

    public JsonMaterialRepository(string? filePath = null, MaterialValidationService? validationService = null)
        : this(new JsonMaterialRepositoryOptions
        {
            DefaultFilePath = filePath,
            ValidationService = validationService
        })
    {
    }

    public JsonMaterialRepository(JsonMaterialRepositoryOptions? options)
    {
        options ??= new JsonMaterialRepositoryOptions();

        _defaultFilePath = NormalizeLibraryPath(ResolveDefaultFilePath(options.DefaultFilePath));
        _validationService = options.ValidationService ?? new MaterialValidationService();
        _locationStore = string.IsNullOrWhiteSpace(options.LocationStoreFilePath)
            ? null
            : new MaterialLibraryLocationStore(NormalizeStorePath(options.LocationStoreFilePath));
        var initialLocation = ResolveInitialActiveFilePath();
        _activeFilePath = initialLocation.ActiveFilePath;
        _locationRequiresSynchronization = initialLocation.RequiresSynchronization;
    }

    public async Task<IReadOnlyList<Material>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var materials = await LoadActiveMaterialsCoreAsync(cancellationToken).ConfigureAwait(false);
            return Sort(materials);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Material?> GetByIdAsync(string materialId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var materials = await LoadActiveMaterialsCoreAsync(cancellationToken).ConfigureAwait(false);
            return materials.FirstOrDefault(material =>
                string.Equals(material.MaterialId, materialId, StringComparison.Ordinal));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Material> CreateAsync(Material material, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var materials = await LoadActiveMaterialsCoreAsync(cancellationToken).ConfigureAwait(false);
            var filePath = _activeFilePath;
            var prepared = _validationService.PrepareForCreate(material, materials);

            if (materials.Any(existing => string.Equals(existing.MaterialId, prepared.MaterialId, StringComparison.Ordinal)))
            {
                throw new MaterialValidationException("material-id-exists", $"Material '{prepared.MaterialId}' already exists.");
            }

            materials.Add(prepared);
            await SaveMaterialsCoreAsync(materials, filePath, cancellationToken).ConfigureAwait(false);
            return prepared;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Material> UpdateAsync(Material material, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var materials = await LoadActiveMaterialsCoreAsync(cancellationToken).ConfigureAwait(false);
            var filePath = _activeFilePath;
            var existingIndex = materials.FindIndex(existing =>
                string.Equals(existing.MaterialId, material.MaterialId, StringComparison.Ordinal));

            if (existingIndex < 0)
            {
                throw new MaterialValidationException("material-not-found", "Material was not found.");
            }

            var prepared = _validationService.PrepareForUpdate(material, materials);
            materials[existingIndex] = prepared;
            await SaveMaterialsCoreAsync(materials, filePath, cancellationToken).ConfigureAwait(false);
            return prepared;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string materialId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(materialId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var materials = await LoadActiveMaterialsCoreAsync(cancellationToken).ConfigureAwait(false);
            var filePath = _activeFilePath;
            var removedCount = materials.RemoveAll(material =>
                string.Equals(material.MaterialId, materialId, StringComparison.Ordinal));

            if (removedCount == 0)
            {
                throw new MaterialValidationException("material-not-found", "Material was not found.");
            }

            await SaveMaterialsCoreAsync(materials, filePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MaterialLibraryLocation> GetLocationAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _ = await LoadActiveMaterialsCoreAsync(cancellationToken).ConfigureAwait(false);
            return CreateLocation(_activeFilePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MaterialLibraryLocation> RepointAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizeLibraryPath(filePath);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await LoadMaterialsCoreAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
            _activeFilePath = normalizedPath;
            _locationRequiresSynchronization = false;
            await PersistLocationCoreAsync(cancellationToken).ConfigureAwait(false);
            return CreateLocation(_activeFilePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MaterialLibraryLocation> RestoreDefaultAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await LoadMaterialsCoreAsync(_defaultFilePath, cancellationToken).ConfigureAwait(false);
            _activeFilePath = _defaultFilePath;
            _locationRequiresSynchronization = false;
            await PersistLocationCoreAsync(cancellationToken).ConfigureAwait(false);
            return CreateLocation(_activeFilePath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<Material>> LoadMaterialsCoreAsync(string filePath, CancellationToken cancellationToken)
    {
        EnsureDirectory(filePath);

        if (!File.Exists(filePath))
        {
            var seeded = SeedMaterials();
            await SaveMaterialsCoreAsync(seeded, filePath, cancellationToken).ConfigureAwait(false);
            return seeded;
        }

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        if (stream.Length == 0)
        {
            var seeded = SeedMaterials();
            await SaveMaterialsCoreAsync(seeded, filePath, cancellationToken).ConfigureAwait(false);
            return seeded;
        }

        try
        {
            var materials = await JsonSerializer.DeserializeAsync<List<Material>>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (materials is not { Count: > 0 })
            {
                var seeded = SeedMaterials();
                await SaveMaterialsCoreAsync(seeded, filePath, cancellationToken).ConfigureAwait(false);
                return seeded;
            }

            try
            {
                return ValidateLoadedMaterials(materials);
            }
            catch (MaterialValidationException exception)
            {
                throw new InvalidDataException(
                    $"Material library file contains invalid material data: {filePath}. {exception.Message}",
                    exception);
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Material library file is not valid JSON: {filePath}", exception);
        }
    }

    private async Task<List<Material>> LoadActiveMaterialsCoreAsync(CancellationToken cancellationToken)
    {
        if (!PathsEqual(_activeFilePath, _defaultFilePath) && !File.Exists(_activeFilePath))
        {
            return await RecoverDefaultLocationCoreAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var materials = await LoadMaterialsCoreAsync(_activeFilePath, cancellationToken).ConfigureAwait(false);
            await BestEffortSynchronizeLocationCoreAsync(cancellationToken, force: false).ConfigureAwait(false);
            return materials;
        }
        catch (Exception exception) when (ShouldRecoverToDefault(exception))
        {
            return await RecoverDefaultLocationCoreAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SaveMaterialsCoreAsync(
        IReadOnlyCollection<Material> materials,
        string filePath,
        CancellationToken cancellationToken)
    {
        EnsureDirectory(filePath);

        var ordered = Sort(materials);
        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, ordered, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private async Task PersistLocationCoreAsync(CancellationToken cancellationToken)
    {
        if (_locationStore is null)
        {
            return;
        }

        if (PathsEqual(_activeFilePath, _defaultFilePath))
        {
            await _locationStore.ClearAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await _locationStore.SaveAsync(_activeFilePath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<Material>> RecoverDefaultLocationCoreAsync(CancellationToken cancellationToken)
    {
        var materials = await LoadMaterialsCoreAsync(_defaultFilePath, cancellationToken).ConfigureAwait(false);
        _activeFilePath = _defaultFilePath;
        await BestEffortSynchronizeLocationCoreAsync(cancellationToken, force: true).ConfigureAwait(false);
        return materials;
    }

    private async Task BestEffortSynchronizeLocationCoreAsync(CancellationToken cancellationToken, bool force)
    {
        if (!force && !_locationRequiresSynchronization)
        {
            return;
        }

        try
        {
            await PersistLocationCoreAsync(cancellationToken).ConfigureAwait(false);
            _locationRequiresSynchronization = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _locationRequiresSynchronization = true;
        }
    }

    private (string ActiveFilePath, bool RequiresSynchronization) ResolveInitialActiveFilePath()
    {
        var storedPath = _locationStore?.TryLoadActiveFilePath();
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return (_defaultFilePath, false);
        }

        try
        {
            var normalizedStoredPath = NormalizeLibraryPath(storedPath);
            return PathsEqual(normalizedStoredPath, _defaultFilePath)
                ? (_defaultFilePath, true)
                : (normalizedStoredPath, false);
        }
        catch (ArgumentException)
        {
            return (_defaultFilePath, true);
        }
    }

    private static string ResolveDefaultFilePath(string? configuredDefaultPath) =>
        string.IsNullOrWhiteSpace(configuredDefaultPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PanelNester",
                "materials.json")
            : configuredDefaultPath;

    private static string NormalizeStorePath(string filePath)
    {
        try
        {
            return Path.GetFullPath(filePath.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("A valid material library location settings path is required.", nameof(filePath), exception);
        }
    }

    private static string NormalizeLibraryPath(string filePath)
    {
        var trimmedPath = filePath?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedPath))
        {
            throw new ArgumentException("A material library file path is required.", nameof(filePath));
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(trimmedPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("A valid material library file path is required.", nameof(filePath), exception);
        }

        if (!string.Equals(Path.GetExtension(normalizedPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Material library files must use a .json extension.", nameof(filePath));
        }

        return normalizedPath;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private bool ShouldRecoverToDefault(Exception exception) =>
        !PathsEqual(_activeFilePath, _defaultFilePath) &&
        exception is InvalidDataException or IOException or UnauthorizedAccessException;

    private void EnsureDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private List<Material> ValidateLoadedMaterials(IEnumerable<Material> materials)
    {
        var normalizedMaterials = new List<Material>();
        var materialIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var material in materials)
        {
            var prepared = _validationService.PrepareForUpdate(material, normalizedMaterials);
            if (!materialIds.Add(prepared.MaterialId))
            {
                throw new InvalidDataException($"Material library file contains duplicate material id '{prepared.MaterialId}'.");
            }

            normalizedMaterials.Add(prepared);
        }

        return normalizedMaterials;
    }

    private MaterialLibraryLocation CreateLocation(string activeFilePath) =>
        new()
        {
            ActiveFilePath = activeFilePath,
            DefaultFilePath = _defaultFilePath,
            UsesDefaultLocation = PathsEqual(activeFilePath, _defaultFilePath)
        };

    private static List<Material> SeedMaterials() =>
        DemoMaterialCatalog.All
            .OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(material => material.MaterialId, StringComparer.Ordinal)
            .ToList();

    private static List<Material> Sort(IEnumerable<Material> materials) =>
        materials
            .OrderBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(material => material.MaterialId, StringComparer.Ordinal)
            .ToList();
}
