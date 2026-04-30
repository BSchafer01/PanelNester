namespace PanelNester.Domain.Models;

public sealed record StiffenerTakeoffRequest
{
    public Project Project { get; init; } = new();
}
