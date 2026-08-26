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
        var location = CreateValidationLocation(update.SourceReferences.FirstOrDefault());
        var rowErrors = new List<string>();
        var rowWarnings = new List<string>();

        var importedId = update.ImportedId?.Trim() ?? string.Empty;
        var lengthText = update.Length?.Trim() ?? string.Empty;
        var widthText = update.Width?.Trim() ?? string.Empty;
        var quantityText = update.Quantity?.Trim() ?? string.Empty;
        var materialName = update.MaterialName?.Trim() ?? string.Empty;
        var group = NormalizeOptional(update.Group);
        var sheetNumber = NormalizeOptional(update.SheetNumber);
        var rowNumber = ParseOptionalPositiveInt(update.RowNumber, "Row Number", "row-number", rowId, location, rowErrors, errors);
        var columnNumber = ParseOptionalPositiveInt(update.ColumnNumber, "Column Number", "column-number", rowId, location, rowErrors, errors);

        if (string.IsNullOrWhiteSpace(importedId))
        {
            AddError("missing-id", "Id is required.", rowId, location, rowErrors, errors);
        }
        else if (!seenIds.Add(importedId))
        {
            AddWarning("duplicate-id", $"Duplicate Id '{importedId}' found.", rowId, location, rowWarnings, warnings);
        }

        if (!TryParseDecimal(lengthText, out var length))
        {
            AddError("invalid-length", "Length must be a decimal value.", rowId, location, rowErrors, errors);
        }
        else if (length <= 0)
        {
            AddError("length-out-of-range", "Length must be greater than zero.", rowId, location, rowErrors, errors);
        }

        if (!TryParseDecimal(widthText, out var width))
        {
            AddError("invalid-width", "Width must be a decimal value.", rowId, location, rowErrors, errors);
        }
        else if (width <= 0)
        {
            AddError("width-out-of-range", "Width must be greater than zero.", rowId, location, rowErrors, errors);
        }

        if (!TryParseInt(quantityText, out var quantity))
        {
            AddError("invalid-quantity", "Quantity must be an integer value.", rowId, location, rowErrors, errors);
        }
        else if (quantity <= 0)
        {
            AddError("quantity-out-of-range", "Quantity must be greater than zero.", rowId, location, rowErrors, errors);
        }
        else if (quantity > LargeQuantityWarningThreshold)
        {
            AddWarning(
                "quantity-large",
                $"Quantity '{quantity}' is very large and may increase nesting time.",
                rowId,
                location,
                rowWarnings,
                warnings);
        }

        if (string.IsNullOrWhiteSpace(materialName))
        {
            AddError("missing-material", "Material is required.", rowId, location, rowErrors, errors);
        }
        else if (!knownMaterials.ContainsKey(materialName))
        {
            AddError(
                "material-not-found",
                $"Material '{materialName}' was not found in the material library.",
                rowId,
                location,
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
            IsManual = update.IsManual,
            SheetNumber = sheetNumber,
            RowNumber = rowNumber,
            ColumnNumber = columnNumber,
            ValidationStatus = validationStatus,
            ValidationMessages = validationMessages,
            SourceReferences = update.SourceReferences
        };
    }

    private static void AddError(
        string code,
        string message,
        string rowId,
        ValidationLocation? location,
        ICollection<string> rowMessages,
        ICollection<ValidationError> errors)
    {
        rowMessages.Add(message);
        errors.Add(new ValidationError(code, message, rowId, location));
    }

    private static void AddWarning(
        string code,
        string message,
        string rowId,
        ValidationLocation? location,
        ICollection<string> rowMessages,
        ICollection<ValidationWarning> warnings)
    {
        rowMessages.Add(message);
        warnings.Add(new ValidationWarning(code, message, rowId, location));
    }

    private static bool TryParseDecimal(string rawValue, out decimal value) =>
        decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static bool TryParseInt(string rawValue, out int value) =>
        int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static int? ParseOptionalPositiveInt(
        string? rawValue,
        string label,
        string codePrefix,
        string rowId,
        ValidationLocation? location,
        ICollection<string> rowErrors,
        ICollection<ValidationError> errors)
    {
        var text = rawValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!TryParseInt(text, out var value))
        {
            AddError($"invalid-{codePrefix}", $"{label} must be an integer value.", rowId, location, rowErrors, errors);
            return null;
        }

        if (value <= 0)
        {
            AddError($"{codePrefix}-out-of-range", $"{label} must be greater than zero.", rowId, location, rowErrors, errors);
            return null;
        }

        return value;
    }

    private static ValidationLocation? CreateValidationLocation(SourceReference? sourceReference) =>
        sourceReference is null
            ? null
            : new ValidationLocation
            {
                WorksheetName = sourceReference.WorksheetName,
                WorksheetPosition = sourceReference.WorksheetPosition,
                PhysicalRow = sourceReference.PhysicalRow
            };

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
