using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

public sealed class StockLengthImportGroupingPlanner
{
    private static readonly IReadOnlySet<string> AllowedFields = new HashSet<string>(StringComparer.Ordinal)
    {
        ImportFieldNames.ProfileNumber,
        ImportFieldNames.Finish,
        ImportFieldNames.PartNumber,
        ImportFieldNames.PartName
    };

    public IReadOnlyList<StockLengthImportGroupPlan> Build(
        StockLengthImportGrouping grouping,
        IReadOnlyList<RequiredPiece> requiredPieces)
    {
        ArgumentNullException.ThrowIfNull(grouping);
        ArgumentNullException.ThrowIfNull(requiredPieces);
        if (grouping.Mode != StockLengthImportGroupingMode.MappedField ||
            string.IsNullOrWhiteSpace(grouping.Field) ||
            !AllowedFields.Contains(grouping.Field))
        {
            throw new StockLengthImportGroupingException(
                "import-grouping-field-invalid",
                "Choose Profile Number, Finish, Part Number, or Part Name as the Grouping Field.");
        }

        var field = grouping.Field;
        var configurations = new Dictionary<string, StockLengthImportGroupConfiguration>(StringComparer.Ordinal);
        var optimizationGroupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var configuration in grouping.Groups)
        {
            var key = Normalize(configuration.GroupingValue);
            if (!configurations.TryAdd(key, configuration))
            {
                throw new StockLengthImportGroupingException(
                    "import-grouping-value-duplicate",
                    $"Grouping Field value '{Display(key, field)}' is configured more than once.");
            }
            if (string.IsNullOrWhiteSpace(configuration.OptimizationGroupId) ||
                string.IsNullOrWhiteSpace(configuration.Name) ||
                configuration.StockLength is not > 0m)
            {
                throw new StockLengthImportGroupingException(
                    "import-grouping-configuration-incomplete",
                    $"Optimization Group '{Display(key, field)}' requires an identity, name, and positive Stock Length.");
            }
            if (!optimizationGroupIds.Add(configuration.OptimizationGroupId))
            {
                throw new StockLengthImportGroupingException(
                    "import-grouping-id-duplicate",
                    "Each field-derived Optimization Group requires a unique identity.");
            }
        }

        var grouped = requiredPieces
            .Select((piece, order) => new { Piece = piece, Order = order, Value = Normalize(GetValue(piece, field)) })
            .GroupBy(item => item.Value, StringComparer.Ordinal)
            .Select(group => new
            {
                Value = group.Key,
                FirstOrder = group.Min(item => item.Order),
                Pieces = (IReadOnlyList<RequiredPiece>)group.OrderBy(item => item.Order).Select(item => item.Piece).ToArray()
            })
            .OrderBy(group => group.Value.Length == 0 ? 1 : 0)
            .ThenBy(group => group.Value, StringComparer.Ordinal)
            .ThenBy(group => group.FirstOrder)
            .ToArray();

        var missing = grouped.FirstOrDefault(group => !configurations.ContainsKey(group.Value));
        if (missing is not null)
        {
            throw new StockLengthImportGroupingException(
                "import-grouping-stock-length-required",
                $"Enter a Stock Length for Optimization Group '{Display(missing.Value, field)}'.");
        }

        return grouped.Select(group => new StockLengthImportGroupPlan
        {
            Key = new ImportGroupingKey { Field = field, NormalizedValue = group.Value },
            Configuration = configurations[group.Value],
            RequiredPieces = group.Pieces
        }).ToArray();
    }

    public static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    public static string Display(string? normalizedValue, string field) =>
        string.IsNullOrWhiteSpace(normalizedValue) ? $"Unspecified {field}" : normalizedValue.Trim();

    public static string? GetValue(RequiredPiece piece, string field) => field switch
    {
        ImportFieldNames.ProfileNumber => piece.ProfileNumber,
        ImportFieldNames.Finish => piece.Finish,
        ImportFieldNames.PartNumber => piece.PartNumber,
        ImportFieldNames.PartName => piece.PartName,
        _ => null
    };
}

public sealed record StockLengthImportGroupPlan
{
    public ImportGroupingKey Key { get; init; } = new();

    public StockLengthImportGroupConfiguration Configuration { get; init; } = new();

    public IReadOnlyList<RequiredPiece> RequiredPieces { get; init; } = Array.Empty<RequiredPiece>();
}

public sealed class StockLengthImportGroupingException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
