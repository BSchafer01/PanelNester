namespace PanelNester.Domain.Models;

public enum WorkbookImportPhase
{
    Preflight,
    OpeningWorkbook,
    InspectingWorksheets,
    ReadingWorksheet,
    Validating,
    CombiningParts,
    Finalizing
}

public sealed record WorkbookPreflightAssessment
{
    public long CompressedBytes { get; init; }

    public long UncompressedBytes { get; init; }

    public int PackageEntryCount { get; init; }

    public long LargestEntryBytes { get; init; }

    public double CompressionRatio { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record WorkbookImportProgress
{
    public WorkbookImportPhase Phase { get; init; }

    public string Label { get; init; } = string.Empty;

    public int? Current { get; init; }

    public int? Total { get; init; }

    public string? WorksheetName { get; init; }

    public WorkbookPreflightAssessment? Preflight { get; init; }

    public bool IsDeterminate => Current is not null && Total is > 0;
}
