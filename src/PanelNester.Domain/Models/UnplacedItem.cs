namespace PanelNester.Domain.Models;

public sealed record UnplacedItem
{
    public string PartId { get; init; } = string.Empty;

    public string ReasonCode { get; init; } = string.Empty;

    public string ReasonDescription { get; init; } = string.Empty;
}
