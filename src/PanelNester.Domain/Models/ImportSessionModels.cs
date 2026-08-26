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
}

public sealed record ImportWorksheetConfiguration
{
    public string WorksheetName { get; init; } = string.Empty;

    public int OriginalPosition { get; init; }

    public string HeadingRange { get; init; } = string.Empty;

    public IReadOnlyList<ImportColumnMapping> ColumnMappings { get; init; } =
        Array.Empty<ImportColumnMapping>();

    public string? OptimizationGroupId { get; init; }

    public IReadOnlyList<int> ExcludedSourceRows { get; init; } = Array.Empty<int>();
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
}

public sealed record WorkbookDiscovery
{
    public string InitialWorksheetName { get; init; } = string.Empty;

    public IReadOnlyList<ImportWorksheetDescriptor> Worksheets { get; init; } =
        Array.Empty<ImportWorksheetDescriptor>();

    public bool MacrosPresent { get; init; }
}

public sealed record SourceReference
{
    public string WorksheetName { get; init; } = string.Empty;

    public int WorksheetPosition { get; init; }

    public int PhysicalRow { get; init; }

    public string SourceFingerprint { get; init; } = string.Empty;
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
