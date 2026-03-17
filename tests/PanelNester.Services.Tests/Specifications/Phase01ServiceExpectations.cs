using System.Globalization;

namespace PanelNester.Services.Tests.Specifications;

internal static class CsvImportSpec
{
    internal static IReadOnlyList<string> RequiredColumns { get; } = ["Id", "Length", "Width", "Quantity", "Material"];

    internal const int LargeQuantityWarningThreshold = 10_000;

    internal static bool HeadersMatch(IEnumerable<string> headers)
    {
        var headerSet = headers.ToHashSet(StringComparer.Ordinal);
        return headerSet.SetEquals(RequiredColumns);
    }

    internal static string? ClassifyRowError(
        string length,
        string width,
        string quantity,
        string material,
        ISet<string> knownMaterials)
    {
        if (!double.TryParse(length, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLength) ||
            !double.TryParse(width, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWidth) ||
            !int.TryParse(quantity, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedQuantity))
        {
            return "invalid-numeric";
        }

        if (parsedLength <= 0d || parsedWidth <= 0d)
        {
            return "non-positive-dimension";
        }

        if (parsedQuantity <= 0)
        {
            return "non-positive-quantity";
        }

        if (string.IsNullOrWhiteSpace(material) || !knownMaterials.Contains(material))
        {
            return "material-not-found";
        }

        return null;
    }
}

internal static class NestingSpec
{
    internal const double ToleranceInches = 0.0001d;

    internal static double Clearance(double spacing, double kerfWidth) => spacing + kerfWidth;

    internal static string? GuardAgainstEmptyRun(int partCount) => partCount > 0 ? null : "empty-run";

    internal static FitDecision EvaluateSinglePart(
        double partLength,
        double partWidth,
        MaterialBounds material)
    {
        var usableLength = material.SheetLength - (2d * material.EdgeMargin);
        var usableWidth = material.SheetWidth - (2d * material.EdgeMargin);

        var fitsWithoutRotation = FitsWithinBounds(partLength, partWidth, usableLength, usableWidth);
        var fitsWithRotation = material.AllowRotation && FitsWithinBounds(partWidth, partLength, usableLength, usableWidth);

        if (fitsWithoutRotation)
        {
            return new FitDecision(Fits: true, RequiresRotation: false, ReasonCode: null);
        }

        if (fitsWithRotation)
        {
            return new FitDecision(Fits: true, RequiresRotation: true, ReasonCode: null);
        }

        return new FitDecision(Fits: false, RequiresRotation: false, ReasonCode: "outside-usable-sheet");
    }

    private static bool FitsWithinBounds(double length, double width, double usableLength, double usableWidth) =>
        length <= usableLength + ToleranceInches &&
        width <= usableWidth + ToleranceInches;
}

internal readonly record struct MaterialBounds(
    double SheetLength,
    double SheetWidth,
    bool AllowRotation,
    double PartSpacing,
    double EdgeMargin);

internal readonly record struct FitDecision(bool Fits, bool RequiresRotation, string? ReasonCode);
