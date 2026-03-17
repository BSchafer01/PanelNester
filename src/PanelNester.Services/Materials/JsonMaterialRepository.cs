using System.Text.Json;
using PanelNester.Domain;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Materials;

public sealed class JsonMaterialRepository : IMaterialRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly MaterialValidationService _validationService;

    public JsonMaterialRepository(string? filePath = null, MaterialValidationService? validationService = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PanelNester",
                "materials.json")
            : filePath;
        _validationService = validationService ?? new MaterialValidationService();
    }

    public async Task<IReadOnlyList<Material>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var materials = await LoadMaterialsCoreAsync(cancellationToken).ConfigureAwait(false);
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
            var materials = await LoadMaterialsCoreAsync(cancellationToken).ConfigureAwait(false);
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
            var materials = await LoadMaterialsCoreAsync(cancellationToken).ConfigureAwait(false);
            var prepared = _validationService.PrepareForCreate(material, materials);

            if (materials.Any(existing => string.Equals(existing.MaterialId, prepared.MaterialId, StringComparison.Ordinal)))
            {
                throw new MaterialValidationException("material-id-exists", $"Material '{prepared.MaterialId}' already exists.");
            }

            materials.Add(prepared);
            await SaveMaterialsCoreAsync(materials, cancellationToken).ConfigureAwait(false);
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
            var materials = await LoadMaterialsCoreAsync(cancellationToken).ConfigureAwait(false);
            var existingIndex = materials.FindIndex(existing =>
                string.Equals(existing.MaterialId, material.MaterialId, StringComparison.Ordinal));

            if (existingIndex < 0)
            {
                throw new MaterialValidationException("material-not-found", "Material was not found.");
            }

            var prepared = _validationService.PrepareForUpdate(material, materials);
            materials[existingIndex] = prepared;
            await SaveMaterialsCoreAsync(materials, cancellationToken).ConfigureAwait(false);
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
            var materials = await LoadMaterialsCoreAsync(cancellationToken).ConfigureAwait(false);
            var removedCount = materials.RemoveAll(material =>
                string.Equals(material.MaterialId, materialId, StringComparison.Ordinal));

            if (removedCount == 0)
            {
                throw new MaterialValidationException("material-not-found", "Material was not found.");
            }

            await SaveMaterialsCoreAsync(materials, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<Material>> LoadMaterialsCoreAsync(CancellationToken cancellationToken)
    {
        EnsureDirectory();

        if (!File.Exists(_filePath))
        {
            var seeded = SeedMaterials();
            await SaveMaterialsCoreAsync(seeded, cancellationToken).ConfigureAwait(false);
            return seeded;
        }

        await using var stream = new FileStream(
            _filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        if (stream.Length == 0)
        {
            var seeded = SeedMaterials();
            await SaveMaterialsCoreAsync(seeded, cancellationToken).ConfigureAwait(false);
            return seeded;
        }

        try
        {
            var materials = await JsonSerializer.DeserializeAsync<List<Material>>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);

            if (materials is not { Count: > 0 })
            {
                var seeded = SeedMaterials();
                await SaveMaterialsCoreAsync(seeded, cancellationToken).ConfigureAwait(false);
                return seeded;
            }

            return ValidateLoadedMaterials(materials);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Material library file is not valid JSON.", exception);
        }
    }

    private async Task SaveMaterialsCoreAsync(IReadOnlyCollection<Material> materials, CancellationToken cancellationToken)
    {
        EnsureDirectory();

        var ordered = Sort(materials);
        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";

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
