using System.Globalization;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

internal static class ImportedPartRowMerger
{
    public static IReadOnlyList<PartRowUpdate> MergeCompatibleRows(
        IReadOnlyList<PartRowUpdate> updates,
        bool useGroupKey,
        bool useSheetNumberKey,
        bool useRowNumberKey,
        bool useColumnNumberKey)
    {
        ArgumentNullException.ThrowIfNull(updates);

        if (updates.Count <= 1)
        {
            return updates;
        }

        var mergedUpdates = new List<PartRowUpdate>(updates.Count);
        var mergedIndexByKey = new Dictionary<MergeKey, int>();

        foreach (var update in updates)
        {
            ArgumentNullException.ThrowIfNull(update);

            if (!TryCreateMergeKey(
                    update,
                    useGroupKey,
                    useSheetNumberKey,
                    useRowNumberKey,
                    useColumnNumberKey,
                    out var mergeKey,
                    out var quantity))
            {
                mergedUpdates.Add(update);
                continue;
            }

            if (!mergedIndexByKey.TryGetValue(mergeKey, out var existingIndex))
            {
                mergedIndexByKey[mergeKey] = mergedUpdates.Count;
                mergedUpdates.Add(update with
                {
                    ImportedId = update.ImportedId.Trim(),
                    MaterialName = update.MaterialName.Trim(),
                    Group = NormalizeOptional(update.Group),
                    SheetNumber = NormalizeOptional(update.SheetNumber),
                    RowNumber = NormalizeOptional(update.RowNumber),
                    ColumnNumber = NormalizeOptional(update.ColumnNumber),
                    Quantity = quantity.ToString(CultureInfo.InvariantCulture)
                });
                continue;
            }

            var existingUpdate = mergedUpdates[existingIndex];
            var summedQuantity = ParsePositiveQuantity(existingUpdate.Quantity) + quantity;

            mergedUpdates[existingIndex] = existingUpdate with
            {
                Quantity = summedQuantity.ToString(CultureInfo.InvariantCulture)
            };
        }

        return mergedUpdates;
    }

    private static bool TryCreateMergeKey(
        PartRowUpdate update,
        bool useGroupKey,
        bool useSheetNumberKey,
        bool useRowNumberKey,
        bool useColumnNumberKey,
        out MergeKey mergeKey,
        out long quantity)
    {
        var importedId = update.ImportedId?.Trim() ?? string.Empty;
        var materialName = update.MaterialName?.Trim() ?? string.Empty;
        var group = NormalizeOptional(update.Group);
        var sheetNumber = NormalizeOptional(update.SheetNumber);
        var rowNumber = NormalizeOptional(update.RowNumber);
        var columnNumber = NormalizeOptional(update.ColumnNumber);

        quantity = 0;
        mergeKey = default;

        if (string.IsNullOrWhiteSpace(importedId) ||
            string.IsNullOrWhiteSpace(materialName) ||
            !TryParsePositiveDecimal(update.Length, out var length) ||
            !TryParsePositiveDecimal(update.Width, out var width) ||
            !TryParsePositiveQuantity(update.Quantity, out quantity))
        {
            return false;
        }

        mergeKey = new MergeKey(
            importedId,
            length,
            width,
            materialName,
            useGroupKey ? group : null,
            useGroupKey,
            useSheetNumberKey ? sheetNumber : null,
            useSheetNumberKey,
            useRowNumberKey ? rowNumber : null,
            useRowNumberKey,
            useColumnNumberKey ? columnNumber : null,
            useColumnNumberKey);
        return true;
    }

    private static bool TryParsePositiveDecimal(string? value, out decimal parsedValue)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out parsedValue) &&
               parsedValue > 0;
    }

    private static bool TryParsePositiveQuantity(string? value, out long parsedValue)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue) &&
               parsedValue > 0;
    }

    private static long ParsePositiveQuantity(string? value)
    {
        if (TryParsePositiveQuantity(value, out var parsedValue))
        {
            return parsedValue;
        }

        throw new InvalidOperationException("Merged import rows must always retain a positive integer quantity.");
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private readonly record struct MergeKey(
        string ImportedId,
        decimal Length,
        decimal Width,
        string MaterialName,
        string? Group,
        bool UsesGroupKey,
        string? SheetNumber,
        bool UsesSheetNumberKey,
        string? RowNumber,
        bool UsesRowNumberKey,
        string? ColumnNumber,
        bool UsesColumnNumberKey);
}
