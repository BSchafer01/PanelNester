namespace PanelNester.Domain.Models;

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

    public string HeadingRangeDetectionStatus { get; init; } = "none";

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
