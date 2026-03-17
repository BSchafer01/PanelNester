using System.Globalization;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

public sealed class PartRowValidator
{
    public static readonly string[] RequiredColumns = [.. ImportFieldNames.Required];

    public const int LargeQuantityWarningThreshold = 10_000;

    public ImportResponse ValidateRows(
        IEnumerable<PartRowUpdate> updates,
        IReadOnlyDictionary<string, Material> knownMaterials,
        IEnumerable<ValidationError>? errors = null,
        IEnumerable<ValidationWarning>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(knownMaterials);

        var parts = new List<PartRow>();
        var errorList = errors?.ToList() ?? [];
        var warningList = warnings?.ToList() ?? [];
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var rowIndex = 0;

        foreach (var update in updates)
        {
            ArgumentNullException.ThrowIfNull(update);

            rowIndex++;
            parts.Add(ValidateRow(update, rowIndex, seenIds, knownMaterials, errorList, warningList));
        }

        return CreateResponse(parts, errorList, warningList);
    }

    internal static ImportResponse CreateResponse(
        IReadOnlyList<PartRow> parts,
        IReadOnlyList<ValidationError> errors,
        IReadOnlyList<ValidationWarning> warnings) =>
        new()
        {
            Success = errors.Count == 0,
            Parts = parts,
            Errors = errors,
            Warnings = warnings
        };

    private static PartRow ValidateRow(
        PartRowUpdate update,
        int rowIndex,
        ISet<string> seenIds,
        IReadOnlyDictionary<string, Material> knownMaterials,
        ICollection<ValidationError> errors,
        ICollection<ValidationWarning> warnings)
    {
        var rowId = string.IsNullOrWhiteSpace(update.RowId)
            ? $"row-{rowIndex}"
            : update.RowId.Trim();
        var rowErrors = new List<string>();
        var rowWarnings = new List<string>();

        var importedId = update.ImportedId?.Trim() ?? string.Empty;
        var lengthText = update.Length?.Trim() ?? string.Empty;
        var widthText = update.Width?.Trim() ?? string.Empty;
        var quantityText = update.Quantity?.Trim() ?? string.Empty;
        var materialName = update.MaterialName?.Trim() ?? string.Empty;
        var group = NormalizeOptional(update.Group);

        if (string.IsNullOrWhiteSpace(importedId))
        {
            AddError("missing-id", "Id is required.", rowId, rowErrors, errors);
        }
        else if (!seenIds.Add(importedId))
        {
            AddWarning("duplicate-id", $"Duplicate Id '{importedId}' found.", rowId, rowWarnings, warnings);
        }

        if (!TryParseDecimal(lengthText, out var length))
        {
            AddError("invalid-length", "Length must be a decimal value.", rowId, rowErrors, errors);
        }
        else if (length <= 0)
        {
            AddError("length-out-of-range", "Length must be greater than zero.", rowId, rowErrors, errors);
        }

        if (!TryParseDecimal(widthText, out var width))
        {
            AddError("invalid-width", "Width must be a decimal value.", rowId, rowErrors, errors);
        }
        else if (width <= 0)
        {
            AddError("width-out-of-range", "Width must be greater than zero.", rowId, rowErrors, errors);
        }

        if (!TryParseInt(quantityText, out var quantity))
        {
            AddError("invalid-quantity", "Quantity must be an integer value.", rowId, rowErrors, errors);
        }
        else if (quantity <= 0)
        {
            AddError("quantity-out-of-range", "Quantity must be greater than zero.", rowId, rowErrors, errors);
        }
        else if (quantity > LargeQuantityWarningThreshold)
        {
            AddWarning(
                "quantity-large",
                $"Quantity '{quantity}' is very large and may increase nesting time.",
                rowId,
                rowWarnings,
                warnings);
        }

        if (string.IsNullOrWhiteSpace(materialName))
        {
            AddError("missing-material", "Material is required.", rowId, rowErrors, errors);
        }
        else if (!knownMaterials.ContainsKey(materialName))
        {
            AddError(
                "material-not-found",
                $"Material '{materialName}' was not found in the material library.",
                rowId,
                rowErrors,
                errors);
        }

        var validationMessages = rowErrors.Concat(rowWarnings).ToArray();
        var validationStatus = rowErrors.Count > 0
            ? ValidationStatuses.Error
            : rowWarnings.Count > 0
                ? ValidationStatuses.Warning
                : ValidationStatuses.Valid;

        return new PartRow
        {
            RowId = rowId,
            ImportedId = importedId,
            LengthText = lengthText,
            Length = length,
            WidthText = widthText,
            Width = width,
            QuantityText = quantityText,
            Quantity = quantity,
            MaterialName = materialName,
            Group = group,
            ValidationStatus = validationStatus,
            ValidationMessages = validationMessages
        };
    }

    private static void AddError(
        string code,
        string message,
        string rowId,
        ICollection<string> rowMessages,
        ICollection<ValidationError> errors)
    {
        rowMessages.Add(message);
        errors.Add(new ValidationError(code, message, rowId));
    }

    private static void AddWarning(
        string code,
        string message,
        string rowId,
        ICollection<string> rowMessages,
        ICollection<ValidationWarning> warnings)
    {
        rowMessages.Add(message);
        warnings.Add(new ValidationWarning(code, message, rowId));
    }

    private static bool TryParseDecimal(string rawValue, out decimal value) =>
        decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static bool TryParseInt(string rawValue, out int value) =>
        int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
