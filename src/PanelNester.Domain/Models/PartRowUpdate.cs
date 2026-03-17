namespace PanelNester.Domain.Models;

public sealed record PartRowUpdate
{
    public string? RowId { get; init; }

    public string ImportedId { get; init; } = string.Empty;

    public string Length { get; init; } = string.Empty;

    public string Width { get; init; } = string.Empty;

    public string Quantity { get; init; } = string.Empty;

    public string MaterialName { get; init; } = string.Empty;

    public string? Group { get; init; }
}
