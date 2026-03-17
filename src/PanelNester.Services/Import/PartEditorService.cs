using System.Globalization;
using System.Text.RegularExpressions;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

public sealed partial class PartEditorService : IPartEditorService
{
    private readonly IReadOnlyList<Material> _fallbackMaterials;
    private readonly IMaterialRepository? _materialRepository;
    private readonly PartRowValidator _validator;

    public PartEditorService(IEnumerable<Material>? knownMaterials = null, PartRowValidator? validator = null)
    {
        _fallbackMaterials = (knownMaterials ?? DemoMaterialCatalog.All).ToArray();
        _validator = validator ?? new PartRowValidator();
    }

    public PartEditorService(IMaterialRepository materialRepository, PartRowValidator? validator = null)
    {
        _materialRepository = materialRepository ?? throw new ArgumentNullException(nameof(materialRepository));
        _fallbackMaterials = Array.Empty<Material>();
        _validator = validator ?? new PartRowValidator();
    }

    public async Task<ImportResponse> AddRowAsync(
        IReadOnlyList<PartRow> parts,
        PartRowUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(update);

        cancellationToken.ThrowIfCancellationRequested();

        var updates = parts.Select(ToUpdate).ToList();
        updates.Add(update with { RowId = CreateNextRowId(parts) });

        return await ValidateAsync(updates, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ImportResponse> UpdateRowAsync(
        IReadOnlyList<PartRow> parts,
        PartRowUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(update);

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(update.RowId))
        {
            return await ValidateAsync(
                    parts.Select(ToUpdate),
                    cancellationToken,
                    [new ValidationError("row-id-required", "A rowId is required when editing a part row.")])
                .ConfigureAwait(false);
        }

        var rowId = update.RowId.Trim();
        var replaced = false;
        var updates = new List<PartRowUpdate>(parts.Count);

        foreach (var part in parts)
        {
            if (string.Equals(part.RowId, rowId, StringComparison.Ordinal))
            {
                updates.Add(update with { RowId = rowId });
                replaced = true;
            }
            else
            {
                updates.Add(ToUpdate(part));
            }
        }

        if (!replaced)
        {
            return await ValidateAsync(
                    updates,
                    cancellationToken,
                    [new ValidationError("row-not-found", $"Part row '{rowId}' was not found.")])
                .ConfigureAwait(false);
        }

        return await ValidateAsync(updates, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ImportResponse> DeleteRowAsync(
        IReadOnlyList<PartRow> parts,
        string rowId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parts);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(rowId))
        {
            return await ValidateAsync(
                    parts.Select(ToUpdate),
                    cancellationToken,
                    [new ValidationError("row-id-required", "A rowId is required when deleting a part row.")])
                .ConfigureAwait(false);
        }

        var trimmedRowId = rowId.Trim();
        var updates = parts
            .Where(part => !string.Equals(part.RowId, trimmedRowId, StringComparison.Ordinal))
            .Select(ToUpdate)
            .ToList();

        if (updates.Count == parts.Count)
        {
            return await ValidateAsync(
                    updates,
                    cancellationToken,
                    [new ValidationError("row-not-found", $"Part row '{trimmedRowId}' was not found.")])
                .ConfigureAwait(false);
        }

        return await ValidateAsync(updates, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImportResponse> ValidateAsync(
        IEnumerable<PartRowUpdate> updates,
        CancellationToken cancellationToken,
        IEnumerable<ValidationError>? errors = null,
        IEnumerable<ValidationWarning>? warnings = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var knownMaterials = await LoadKnownMaterialsAsync(cancellationToken).ConfigureAwait(false);
        return _validator.ValidateRows(updates, knownMaterials, errors, warnings);
    }

    private async Task<IReadOnlyDictionary<string, Material>> LoadKnownMaterialsAsync(CancellationToken cancellationToken)
    {
        var materials = _materialRepository is not null
            ? await _materialRepository.GetAllAsync(cancellationToken).ConfigureAwait(false)
            : _fallbackMaterials;

        return materials
            .GroupBy(material => material.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    private static PartRowUpdate ToUpdate(PartRow row) =>
        new()
        {
            RowId = row.RowId,
            ImportedId = row.ImportedId,
            Length = row.LengthText ?? row.Length.ToString(CultureInfo.InvariantCulture),
            Width = row.WidthText ?? row.Width.ToString(CultureInfo.InvariantCulture),
            Quantity = row.QuantityText ?? row.Quantity.ToString(CultureInfo.InvariantCulture),
            MaterialName = row.MaterialName,
            Group = row.Group
        };

    private static string CreateNextRowId(IReadOnlyList<PartRow> parts)
    {
        var rowMatches = parts
            .Select(part => RowIdSuffixRegex().Match(part.RowId ?? string.Empty))
            .Where(match => match.Success)
            .Select(match => new
            {
                Value = int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture),
                Width = match.Groups["number"].Value.Length
            })
            .ToArray();

        var nextValue = rowMatches.Length == 0 ? parts.Count + 1 : rowMatches.Max(match => match.Value) + 1;
        var width = rowMatches.Length == 0 ? 0 : rowMatches.Max(match => match.Width);

        return width > 1
            ? $"row-{nextValue.ToString($"D{width}", CultureInfo.InvariantCulture)}"
            : $"row-{nextValue.ToString(CultureInfo.InvariantCulture)}";
    }

    [GeneratedRegex(@"(?<number>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex RowIdSuffixRegex();
}
