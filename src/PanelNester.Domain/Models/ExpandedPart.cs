namespace PanelNester.Domain.Models;

public sealed record ExpandedPart
{
    public string InstanceId { get; init; } = string.Empty;

    public string SourceRowId { get; init; } = string.Empty;

    public string PartId { get; init; } = string.Empty;

    public decimal Length { get; init; }

    public decimal Width { get; init; }

    public string MaterialName { get; init; } = string.Empty;

    public string? Group { get; init; }
}
