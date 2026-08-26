namespace PanelNester.Domain.Models;

public static class HeadingRangeDetectionStatuses
{
    public const string None = "none";

    public const string LowConfidence = "low-confidence";

    public const string Tied = "tied";

    public const string UniqueHighConfidence = "unique-high-confidence";
}

public sealed record ImportConfiguration
{
    public ImportOptions Options { get; init; } = new();

    public IReadOnlyList<ImportWorksheetConfiguration> Worksheets { get; init; } =
        Array.Empty<ImportWorksheetConfiguration>();

    public IReadOnlyList<PartOverride> PartOverrides { get; init; } =
        Array.Empty<PartOverride>();
}

public sealed record ImportWorksheetConfiguration
{
    public string WorksheetName { get; init; } = string.Empty;

    public int OriginalPosition { get; init; }

    public string HeadingRange { get; init; } = string.Empty;

    public IReadOnlyList<ImportColumnMapping> ColumnMappings { get; init; } =
        Array.Empty<ImportColumnMapping>();

    public string? OptimizationGroupId { get; init; }

    public IReadOnlyList<ExcludedSourceRow> ExcludedSourceRows { get; init; } =
        Array.Empty<ExcludedSourceRow>();
}

public sealed record PartOverride
{
    public string RowId { get; init; } = string.Empty;

    public PartRow ImportedValues { get; init; } = new();

    public PartRow CurrentValues { get; init; } = new();

    public IReadOnlyList<SourceReference> SourceReferences { get; init; } =
        Array.Empty<SourceReference>();
}

public sealed record ExcludedSourceRow
{
    public string RowId { get; init; } = string.Empty;

    public SourceReference SourceReference { get; init; } = new();

    public SourceRowValidationError OriginalValidationError { get; init; } = new();
}

public sealed record SourceRowValidationError
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

public sealed record ImportWorksheetDescriptor
{
    public string WorksheetName { get; init; } = string.Empty;

    public int OriginalPosition { get; init; }

    public string HeadingRange { get; init; } = string.Empty;

    public string HeadingRangeDetectionStatus { get; init; } = HeadingRangeDetectionStatuses.None;

    public IReadOnlyList<HeadingRangeCandidate> HeadingRangeCandidates { get; init; } =
        Array.Empty<HeadingRangeCandidate>();

    public IReadOnlyList<WorksheetPreviewRow> PreviewRows { get; init; } =
        Array.Empty<WorksheetPreviewRow>();
}

public sealed record HeadingRangeCandidate
{
    public string Address { get; init; } = string.Empty;

    public double Confidence { get; init; }

    public bool IsHighConfidence { get; init; }

    public bool IsTied { get; init; }
}

public sealed record WorksheetPreviewRow
{
    public int RowNumber { get; init; }

    public IReadOnlyList<WorksheetPreviewCell> Cells { get; init; } =
        Array.Empty<WorksheetPreviewCell>();
}

public sealed record WorksheetPreviewCell
{
    public string Address { get; init; } = string.Empty;

    public int ColumnNumber { get; init; }

    public string Value { get; init; } = string.Empty;

    public bool IsHidden { get; init; }

    public bool IsFormula { get; init; }
}

public sealed record WorkbookDiscovery
{
    public string InitialWorksheetName { get; init; } = string.Empty;

    public IReadOnlyList<ImportWorksheetDescriptor> Worksheets { get; init; } =
        Array.Empty<ImportWorksheetDescriptor>();

    public bool MacrosPresent { get; init; }
}

public record WorksheetRowLocation
{
    public string WorksheetName { get; init; } = string.Empty;

    public int WorksheetPosition { get; init; }

    public int PhysicalRow { get; init; }
}

public sealed record SourceReference : WorksheetRowLocation
{

    public string SourceFingerprint { get; init; } = string.Empty;

    public bool MatchesIdentity(SourceReference other) =>
        other is not null &&
        WorksheetPosition == other.WorksheetPosition &&
        PhysicalRow == other.PhysicalRow &&
        !string.IsNullOrWhiteSpace(SourceFingerprint) &&
        string.Equals(SourceFingerprint, other.SourceFingerprint, StringComparison.Ordinal);
}

public sealed record ImportSourceMetadata
{
    public string ImportSourcePath { get; init; } = string.Empty;

    public string ContentFingerprint { get; init; } = string.Empty;

    public long ContentLength { get; init; }

    public DateTime SnapshotCapturedAtUtc { get; init; }
}

public sealed record ImportPreviewSummary
{
    public IReadOnlyList<ImportWorksheetPreviewSummary> Worksheets { get; init; } =
        Array.Empty<ImportWorksheetPreviewSummary>();

    public IReadOnlyList<ImportOptimizationGroupPreviewSummary> OptimizationGroups { get; init; } =
        Array.Empty<ImportOptimizationGroupPreviewSummary>();
}

public sealed record ImportWorksheetPreviewSummary
{
    public string WorksheetName { get; init; } = string.Empty;

    public int OriginalPosition { get; init; }

    public int SourceRowCount { get; init; }

    public int ImportedPartCount { get; init; }

    public int ExcludedRowCount { get; init; }

    public int IssueCount { get; init; }
}

public sealed record ImportOptimizationGroupPreviewSummary
{
    public string OptimizationGroupId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int SourceRowCount { get; init; }

    public int CombinedPartCount { get; init; }

    public int MergedRowCount { get; init; }
}
