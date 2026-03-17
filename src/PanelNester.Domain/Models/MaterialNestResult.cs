namespace PanelNester.Domain.Models;

public sealed record MaterialNestResult
{
    public string MaterialName { get; init; } = string.Empty;

    public string? MaterialId { get; init; }

    public NestResponse Result { get; init; } = new();
}
