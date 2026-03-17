using PanelNester.Domain;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Materials;

public sealed class MaterialService : IMaterialService
{
    private readonly Func<string> _idGenerator;
    private readonly IMaterialRepository _repository;

    public MaterialService(IMaterialRepository repository, Func<string>? idGenerator = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _idGenerator = idGenerator ?? (() => Guid.NewGuid().ToString("N"));
    }

    public Task<IReadOnlyList<Material>> ListAsync(CancellationToken cancellationToken = default) =>
        _repository.GetAllAsync(cancellationToken);

    public async Task<MaterialOperationResult> GetAsync(string materialId, CancellationToken cancellationToken = default)
    {
        var persistedMaterial = string.IsNullOrWhiteSpace(materialId)
            ? null
            : await _repository.GetByIdAsync(materialId.Trim(), cancellationToken).ConfigureAwait(false);

        return persistedMaterial is null
            ? Failure("material-not-found", $"Material '{materialId?.Trim()}' was not found.")
            : Success(persistedMaterial);
    }

    public async Task<MaterialOperationResult> CreateAsync(Material material, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(material);

        try
        {
            var existingMaterials = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var materialWithId = string.IsNullOrWhiteSpace(material.MaterialId)
                ? material with { MaterialId = CreateUniqueMaterialId(existingMaterials) }
                : material;

            var createdMaterial = await _repository.CreateAsync(materialWithId, cancellationToken).ConfigureAwait(false);
            return Success(createdMaterial);
        }
        catch (MaterialValidationException exception)
        {
            return Failure(exception.Code, exception.Message);
        }
    }

    public async Task<MaterialOperationResult> UpdateAsync(Material material, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(material);

        try
        {
            var updatedMaterial = await _repository.UpdateAsync(material, cancellationToken).ConfigureAwait(false);
            return Success(updatedMaterial);
        }
        catch (MaterialValidationException exception)
        {
            return Failure(exception.Code, exception.Message);
        }
    }

    public async Task<MaterialDeleteResult> DeleteAsync(
        string materialId,
        bool isInUse = false,
        CancellationToken cancellationToken = default)
    {
        if (isInUse)
        {
            return new MaterialDeleteResult
            {
                Success = false,
                Errors =
                [
                    new ValidationError(
                        "material-in-use",
                        "Material is in use and cannot be deleted until project references are removed.")
                ]
            };
        }

        try
        {
            await _repository.DeleteAsync(materialId, cancellationToken).ConfigureAwait(false);
            return new MaterialDeleteResult { Success = true };
        }
        catch (MaterialValidationException exception)
        {
            return new MaterialDeleteResult
            {
                Success = false,
                Errors = [new ValidationError(exception.Code, exception.Message)]
            };
        }
    }

    private string CreateUniqueMaterialId(IReadOnlyList<Material> existingMaterials)
    {
        var knownIds = existingMaterials
            .Select(material => material.MaterialId)
            .Where(materialId => !string.IsNullOrWhiteSpace(materialId))
            .ToHashSet(StringComparer.Ordinal);

        var generatedId = _idGenerator().Trim();
        if (!string.IsNullOrWhiteSpace(generatedId) && knownIds.Add(generatedId))
        {
            return generatedId;
        }

        string fallbackId;
        do
        {
            fallbackId = Guid.NewGuid().ToString("N");
        }
        while (!knownIds.Add(fallbackId));

        return fallbackId;
    }

    private static MaterialOperationResult Success(Material material) =>
        new()
        {
            Success = true,
            Material = material
        };

    private static MaterialOperationResult Failure(string code, string message) =>
        new()
        {
            Success = false,
            Errors = [new ValidationError(code, message)]
        };
}
