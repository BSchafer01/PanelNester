namespace PanelNester.Domain.Models;

public sealed record RequiredPiece
{
    public string RequiredPieceId { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public string? QuantityText { get; init; }

    public decimal Length { get; init; }

    public string? LengthText { get; init; }

    public string ProfileNumber { get; init; } = string.Empty;

    public string? PartName { get; init; }

    public string? Finish { get; init; }

    public string? PartNumber { get; init; }

    public bool IsManual { get; init; } = true;

    public string ValidationStatus { get; init; } = ValidationStatuses.Valid;

    public IReadOnlyList<string> ValidationMessages { get; init; } = Array.Empty<string>();

    public IReadOnlyList<SourceReference> SourceReferences { get; init; } = Array.Empty<SourceReference>();

    public bool HasSameOptimizationInputs(RequiredPiece other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Quantity == other.Quantity &&
               Length == other.Length &&
               string.Equals(ProfileNumber.Trim(), other.ProfileNumber.Trim(), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeOptional(Finish), NormalizeOptional(other.Finish), StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
