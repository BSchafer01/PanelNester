namespace PanelNester.Domain.Models;

public enum StockLengthReportState
{
    Complete,
    Partial,
    Failed,
    ApplicationError,
    Empty,
    NeedsGeneration
}

public sealed record StockLengthReportScope
{
    public string? OptimizationGroupId { get; init; }

    public bool HasStockGroupFilter { get; init; }

    public string? StockGroupProfileNumber { get; init; }

    public string? StockGroupFinish { get; init; }
}

public sealed record StockLengthReportDataRequest
{
    public Project Project { get; init; } = new();

    public StockLengthReportScope Scope { get; init; } = new();
}

public sealed record StockLengthReportData
{
    public string? CompanyLogoPath { get; init; }

    public ReportSettings Settings { get; init; } = new();

    public ProjectMetadata ProjectMetadata { get; init; } = new();

    public InchDisplayFormat InchDisplayFormat { get; init; } = InchDisplayFormat.Decimal;

    public StockLengthReportScope Scope { get; init; } = new();

    public StockLengthReportSummary Summary { get; init; } = new();

    public IReadOnlyList<StockLengthReportOptimizationGroup> OptimizationGroups { get; init; } =
        Array.Empty<StockLengthReportOptimizationGroup>();

    public IReadOnlyList<StockLengthReportUnplacedPieceInstance> UnplacedPieceInstances { get; init; } =
        Array.Empty<StockLengthReportUnplacedPieceInstance>();

}

public sealed record StockLengthReportOptimizationGroup
{
    public string OptimizationGroupId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Order { get; init; }

    public StockLengthReportState State { get; init; }

    public string? FailureMessage { get; init; }

    public StockLengthReportSummary Summary { get; init; } = new();

    public IReadOnlyList<StockLengthReportStockGroup> StockGroups { get; init; } =
        Array.Empty<StockLengthReportStockGroup>();
}

public sealed record StockLengthReportStockGroup
{
    public string ProfileNumber { get; init; } = string.Empty;

    public string? Finish { get; init; }

    public StockLengthReportState State { get; init; }

    public StockLengthReportSummary Summary { get; init; } = new();

    public IReadOnlyList<StockLengthReportStockItem> StockItems { get; init; } =
        Array.Empty<StockLengthReportStockItem>();
}

public sealed record StockLengthReportSummary
{
    public int AcceptedPieceInstanceCount { get; init; }

    public int PlacedPieceInstanceCount { get; init; }

    public int UnplacedPieceInstanceCount { get; init; }

    public decimal StockLength { get; init; }

    public decimal PieceLength { get; init; }

    public decimal SawLoss { get; init; }

    public decimal Remainder { get; init; }

    public decimal UtilizationPercent { get; init; }
}

public sealed record StockLengthReportStockItem
{
    public int StockItemNumber { get; init; }

    public StockItemKind Kind { get; init; } = StockItemKind.Regular;

    public decimal StockLength { get; init; }

    public decimal PieceLength { get; init; }

    public decimal SawLoss { get; init; }

    public decimal Remainder { get; init; }

    public decimal UtilizationPercent { get; init; }

    public IReadOnlyList<StockLengthReportPieceInstance> CutSequence { get; init; } =
        Array.Empty<StockLengthReportPieceInstance>();
}

public sealed record StockLengthReportPieceInstance
{
    public string PieceInstanceId { get; init; } = string.Empty;

    public string RequiredPieceId { get; init; } = string.Empty;

    public int QuantityInstance { get; init; }

    public int Sequence { get; init; }

    public decimal Length { get; init; }

    public string ProfileNumber { get; init; } = string.Empty;

    public string? Finish { get; init; }

    public string? PartNumber { get; init; }

    public string? PartName { get; init; }

    public IReadOnlyList<SourceReference> SourceReferences { get; init; } = Array.Empty<SourceReference>();
}

public sealed record StockLengthReportUnplacedPieceInstance
{
    public string OptimizationGroupId { get; init; } = string.Empty;

    public string OptimizationGroupName { get; init; } = string.Empty;

    public int OptimizationGroupOrder { get; init; }

    public string ProfileNumber { get; init; } = string.Empty;

    public string? Finish { get; init; }

    public StockLengthReportState State { get; init; }

    public StockLengthReportPieceInstance PieceInstance { get; init; } = new();

    public string ReasonCode { get; init; } = string.Empty;

    public string ReasonDescription { get; init; } = string.Empty;
}
