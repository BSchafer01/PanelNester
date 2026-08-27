namespace PanelNester.Domain.Models;

public sealed record NestRequest
{
    public IReadOnlyList<PartRow> Parts { get; init; } = Array.Empty<PartRow>();

    public Material Material { get; init; } = DemoMaterialCatalog.Phase1;

    public decimal KerfWidth { get; init; }
}

public enum NestingProgressPhase
{
    Preparing,
    Placing
}

public sealed record NestingProgress
{
    public NestingProgressPhase Phase { get; init; }

    public long CompletedItems { get; init; }

    public long TotalItems { get; init; }
}
