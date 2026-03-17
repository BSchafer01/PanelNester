namespace PanelNester.Domain.Models;

public sealed record NestRequest
{
    public IReadOnlyList<PartRow> Parts { get; init; } = Array.Empty<PartRow>();

    public Material Material { get; init; } = DemoMaterialCatalog.Phase1;

    public decimal KerfWidth { get; init; }
}
