using PanelNester.Domain;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Materials;

public sealed class MaterialValidationService
{
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    public MaterialValidationResult ValidateForCreate(Material candidate, IReadOnlyCollection<Material> existingMaterials) =>
        Validate(candidate, existingMaterials, requireMaterialId: false);

    public MaterialValidationResult ValidateForUpdate(Material candidate, IReadOnlyCollection<Material> existingMaterials) =>
        Validate(candidate, existingMaterials, requireMaterialId: true);

    public Material PrepareForCreate(Material candidate, IReadOnlyCollection<Material> existingMaterials)
    {
        var normalizedCandidate = Normalize(candidate, generateId: true);
        var result = ValidateForCreate(normalizedCandidate, existingMaterials);
        return result.IsValid
            ? result.Material
            : throw ToException(result.Errors[0]);
    }

    public Material PrepareForUpdate(Material candidate, IReadOnlyCollection<Material> existingMaterials)
    {
        var result = ValidateForUpdate(candidate, existingMaterials);
        return result.IsValid
            ? result.Material
            : throw ToException(result.Errors[0]);
    }

    private static Material Normalize(Material material, bool generateId)
    {
        var materialId = material.MaterialId.Trim();

        if (generateId && string.IsNullOrWhiteSpace(materialId))
        {
            materialId = Guid.NewGuid().ToString("N");
        }

        return material with
        {
            MaterialId = materialId,
            Name = material.Name.Trim(),
            ColorFinish = NormalizeOptional(material.ColorFinish),
            Notes = NormalizeOptional(material.Notes)
        };
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static MaterialValidationResult Validate(
        Material candidate,
        IReadOnlyCollection<Material> existingMaterials,
        bool requireMaterialId)
    {
        var material = Normalize(candidate, generateId: false);
        var errors = new List<ValidationError>();

        if (requireMaterialId && string.IsNullOrWhiteSpace(material.MaterialId))
        {
            errors.Add(new ValidationError("material-not-found", "Material was not found."));
        }

        if (string.IsNullOrWhiteSpace(material.Name))
        {
            errors.Add(new ValidationError("material-name-required", "Material name is required."));
        }

        if (material.SheetLength <= 0)
        {
            errors.Add(new ValidationError("sheet-length-out-of-range", "Sheet length must be greater than zero."));
        }

        if (material.SheetWidth <= 0)
        {
            errors.Add(new ValidationError("sheet-width-out-of-range", "Sheet width must be greater than zero."));
        }

        if (material.DefaultSpacing < 0)
        {
            errors.Add(new ValidationError("default-spacing-out-of-range", "Default spacing cannot be negative."));
        }

        if (material.DefaultEdgeMargin < 0)
        {
            errors.Add(new ValidationError("default-edge-margin-out-of-range", "Default edge margin cannot be negative."));
        }

        if (material.CostPerSheet is < 0)
        {
            errors.Add(new ValidationError("cost-per-sheet-out-of-range", "Cost per sheet cannot be negative."));
        }

        var nameExists = existingMaterials.Any(existing =>
            !string.Equals(existing.MaterialId, material.MaterialId, StringComparison.Ordinal) &&
            NameComparer.Equals(existing.Name.Trim(), material.Name));

        if (nameExists)
        {
            errors.Add(new ValidationError(
                "material-name-exists",
                $"Material '{material.Name}' already exists."));
        }

        return new MaterialValidationResult
        {
            Material = material,
            Errors = errors
        };
    }

    private static MaterialValidationException ToException(ValidationError error) =>
        new(error.Code, error.Message);
}
