namespace PanelNester.Domain.Models;

public sealed record PartRow
{
    public string RowId { get; init; } = string.Empty;

    public string ImportedId { get; init; } = string.Empty;

    public string? LengthText { get; init; }

    public decimal Length { get; init; }

    public string? WidthText { get; init; }

    public decimal Width { get; init; }

    public string? QuantityText { get; init; }

    public int Quantity { get; init; }

    public string MaterialName { get; init; } = string.Empty;

    public string? Group { get; init; }

    public string ValidationStatus { get; init; } = ValidationStatuses.Valid;

    public IReadOnlyList<string> ValidationMessages { get; init; } = Array.Empty<string>();

    public bool Equals(PartRow? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return string.Equals(RowId, other.RowId, StringComparison.Ordinal) &&
               string.Equals(ImportedId, other.ImportedId, StringComparison.Ordinal) &&
               string.Equals(LengthText, other.LengthText, StringComparison.Ordinal) &&
               Length == other.Length &&
               string.Equals(WidthText, other.WidthText, StringComparison.Ordinal) &&
               Width == other.Width &&
               string.Equals(QuantityText, other.QuantityText, StringComparison.Ordinal) &&
               Quantity == other.Quantity &&
               string.Equals(MaterialName, other.MaterialName, StringComparison.Ordinal) &&
               string.Equals(Group, other.Group, StringComparison.Ordinal) &&
               string.Equals(ValidationStatus, other.ValidationStatus, StringComparison.Ordinal) &&
               ValidationMessages.SequenceEqual(other.ValidationMessages, StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RowId, StringComparer.Ordinal);
        hash.Add(ImportedId, StringComparer.Ordinal);
        hash.Add(LengthText, StringComparer.Ordinal);
        hash.Add(Length);
        hash.Add(WidthText, StringComparer.Ordinal);
        hash.Add(Width);
        hash.Add(QuantityText, StringComparer.Ordinal);
        hash.Add(Quantity);
        hash.Add(MaterialName, StringComparer.Ordinal);
        hash.Add(Group, StringComparer.Ordinal);
        hash.Add(ValidationStatus, StringComparer.Ordinal);

        foreach (var validationMessage in ValidationMessages)
        {
            hash.Add(validationMessage, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
