namespace PanelNester.Domain.Models;

public static class DemoMaterialCatalog
{
    public static Material Phase1 { get; } = new()
    {
        MaterialId = "demo-material",
        Name = "Demo Material",
        SheetLength = 96m,
        SheetWidth = 48m,
        AllowRotation = true,
        DefaultSpacing = 0.125m,
        DefaultEdgeMargin = 0.5m,
        ColorFinish = "Phase 2 seed",
        Notes = "Seeded into the local material library on first run."
    };

    public static IReadOnlyList<Material> All { get; } = new[] { Phase1 };
}
