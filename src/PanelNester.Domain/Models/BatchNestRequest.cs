namespace PanelNester.Domain.Models;

public sealed record BatchNestRequest
{
    public IReadOnlyList<PartRow> Parts { get; init; } = Array.Empty<PartRow>();

    public IReadOnlyList<Material> Materials { get; init; } = Array.Empty<Material>();

    public decimal KerfWidth { get; init; }

    public string? SelectedMaterialId { get; init; }
}
