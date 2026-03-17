using PanelNester.Domain.Models;

namespace PanelNester.Domain.Tests.Specifications;

internal static class Phase02MaterialExpectations
{
    internal static IReadOnlyList<string> CrudErrorCodes { get; } =
    [
        "material-not-found",
        "material-name-exists",
        "material-in-use"
    ];

    internal static bool HasUniqueNames(IEnumerable<Material> materials) =>
        materials
            .Select(material => material.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == materials.Count();

    internal static Material CreateSampleMaterial() => new()
    {
        MaterialId = "mat-baltic-birch",
        Name = "Baltic Birch",
        SheetLength = 120m,
        SheetWidth = 60m,
        AllowRotation = true,
        DefaultSpacing = 0.125m,
        DefaultEdgeMargin = 0.5m,
        ColorFinish = "Clear",
        Notes = "Phase 2 sample material for JSON persistence tests.",
        CostPerSheet = 142.75m
    };
}
