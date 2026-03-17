namespace PanelNester.Domain.Models;

public sealed record NestResponse
{
    public bool Success { get; init; }

    public IReadOnlyList<NestSheet> Sheets { get; init; } = Array.Empty<NestSheet>();

    public IReadOnlyList<NestPlacement> Placements { get; init; } = Array.Empty<NestPlacement>();

    public IReadOnlyList<UnplacedItem> UnplacedItems { get; init; } = Array.Empty<UnplacedItem>();

    public MaterialSummary Summary { get; init; } = new();

    public bool Equals(NestResponse? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return Success == other.Success &&
               Sheets.SequenceEqual(other.Sheets) &&
               Placements.SequenceEqual(other.Placements) &&
               UnplacedItems.SequenceEqual(other.UnplacedItems) &&
               EqualityComparer<MaterialSummary>.Default.Equals(Summary, other.Summary);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Success);

        foreach (var sheet in Sheets)
        {
            hash.Add(sheet);
        }

        foreach (var placement in Placements)
        {
            hash.Add(placement);
        }

        foreach (var unplacedItem in UnplacedItems)
        {
            hash.Add(unplacedItem);
        }

        hash.Add(Summary);
        return hash.ToHashCode();
    }
}
