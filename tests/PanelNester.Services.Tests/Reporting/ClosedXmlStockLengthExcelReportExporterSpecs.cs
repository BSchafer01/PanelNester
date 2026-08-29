using ClosedXML.Excel;
using PanelNester.Domain.Models;
using PanelNester.Services.Reporting;

namespace PanelNester.Services.Tests.Reporting;

public sealed class ClosedXmlStockLengthExcelReportExporterSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(
        Path.GetTempPath(),
        $"PanelNester.ClosedXmlStockLengthExcelReportExporterSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Export_stock_length_async_writes_filterable_physical_rows_in_domain_order()
    {
        var filePath = Path.Combine(_workspacePath, "stock-length.xlsx");
        var project = new Project
        {
            ProjectKind = ProjectKind.StockLength,
            Settings = new ProjectSettings { InchDisplayFormat = InchDisplayFormat.Fractional16 },
            State = new ProjectState
            {
                OptimizationGroups =
                [
                    CreateGroup(
                        "group-b",
                        "Second Group",
                        1,
                        "Z-200",
                        "Mill",
                        CutPlanStatus.Partial,
                        stockItemNumber: 2,
                        pieceId: "piece-z",
                        pieceLength: 30m,
                        sourceWorksheet: "Later",
                        sourceRow: 8,
                        unplacedPieceId: "piece-z-unplaced",
                        kind: StockItemKind.Oversized),
                    CreateGroup(
                        "group-a",
                        "First Group",
                        0,
                        "A-100",
                        "Black",
                        CutPlanStatus.Complete,
                        stockItemNumber: 1,
                        pieceId: "piece-a",
                        pieceLength: 24.125m,
                        sourceWorksheet: "Pieces",
                        sourceRow: 4)
                ]
            }
        };

        var report = await new StockLengthReportDataService().BuildAsync(
            new StockLengthReportDataRequest
            {
                Project = project,
                Scope = new StockLengthReportScope()
            });
        await new ClosedXmlExcelReportExporter().ExportAsync(report, filePath);

        using var workbook = new XLWorkbook(filePath);
        Assert.Equal(["Summary", "Cut Plans", "Unplaced"], workbook.Worksheets.Select(sheet => sheet.Name));

        var summary = workbook.Worksheet("Summary");
        Assert.Equal(
            ["Optimization Group", "Stock Group", "Profile Number", "Finish", "Stock Item", "Stock Type", "Placed Piece Instances", "Stock Length", "Piece Length", "Saw Loss", "Remainder", "Utilization", "Status"],
            summary.Row(1).Cells(1, 13).Select(cell => cell.GetString()));
        Assert.Equal(2, summary.Tables.Count());
        Assert.Equal("First Group", summary.Cell(2, 1).GetString());
        Assert.Equal(1, summary.Cell(2, 5).GetValue<int>());
        Assert.Equal("Regular", summary.Cell(2, 6).GetString());
        Assert.Equal(1, summary.Cell(2, 7).GetValue<int>());
        Assert.Equal(120m, summary.Cell(2, 8).GetValue<decimal>());
        Assert.Equal(24.125m, summary.Cell(2, 9).GetValue<decimal>());
        Assert.Equal(0m, summary.Cell(2, 10).GetValue<decimal>());
        Assert.Equal(95.875m, summary.Cell(2, 11).GetValue<decimal>());
        Assert.Equal(
            decimal.Round(24.125m / 120m, 12),
            decimal.Round(summary.Cell(2, 12).GetValue<decimal>(), 12));
        Assert.Equal("Complete", summary.Cell(2, 13).GetString());
        Assert.Equal("Second Group", summary.Cell(3, 1).GetString());
        Assert.Equal("Oversized", summary.Cell(3, 6).GetString());
        Assert.Equal("Partial", summary.Cell(3, 13).GetString());

        var cuts = workbook.Worksheet("Cut Plans");
        Assert.Equal(
            ["Optimization Group", "Stock Group", "Profile Number", "Finish", "Stock Item", "Stock Type", "Cut Sequence", "Piece Instance", "Quantity Instance", "Required Piece", "Part Number", "Part Name", "Length", "Start Position", "End Position", "Source References", "Status"],
            cuts.Row(1).Cells(1, 17).Select(cell => cell.GetString()));
        Assert.Single(cuts.Tables);
        Assert.Equal("First Group", cuts.Cell(2, 1).GetString());
        Assert.Equal("Regular", cuts.Cell(2, 6).GetString());
        Assert.Equal(1, cuts.Cell(2, 7).GetValue<int>());
        Assert.Equal(24.125m, cuts.Cell(2, 13).GetValue<decimal>());
        Assert.Equal(0m, cuts.Cell(2, 14).GetValue<decimal>());
        Assert.Equal(24.125m, cuts.Cell(2, 15).GetValue<decimal>());
        Assert.Equal("Pieces!4", cuts.Cell(2, 16).GetString());

        var unplaced = workbook.Worksheet("Unplaced");
        Assert.Equal(
            ["Optimization Group", "Stock Group", "Profile Number", "Finish", "Piece Instance", "Quantity Instance", "Required Piece", "Part Number", "Part Name", "Length", "Source References", "Reason Code", "Reason", "Status"],
            unplaced.Row(1).Cells(1, 14).Select(cell => cell.GetString()));
        Assert.Single(unplaced.Tables);
        Assert.Equal("Second Group", unplaced.Cell(2, 1).GetString());
        Assert.Equal("piece-z-unplaced", unplaced.Cell(2, 5).GetString());
        Assert.Equal("too-long", unplaced.Cell(2, 12).GetString());
    }

    [Fact]
    public async Task Export_stock_length_async_keeps_incomplete_states_and_empty_worksheets_filterable()
    {
        var filePath = Path.Combine(_workspacePath, "incomplete-stock-length.xlsx");
        var project = new Project
        {
            ProjectKind = ProjectKind.StockLength,
            State = new ProjectState
            {
                OptimizationGroups =
                [
                    new OptimizationGroup { OptimizationGroupId = "empty", Name = "Empty Group", Order = 0 },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "stale",
                        Name = "Changed Group",
                        Order = 1,
                        ResultStatus = OptimizationResultStatus.Stale,
                        RequiredPieces =
                        [
                            new RequiredPiece
                            {
                                RequiredPieceId = "required-stale",
                                Quantity = 1,
                                Length = 20m,
                                ProfileNumber = "P-100"
                            }
                        ]
                    },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "failed",
                        Name = "Failed Group",
                        Order = 2,
                        ResultStatus = OptimizationResultStatus.Valid,
                        RequiredPieces =
                        [
                            new RequiredPiece
                            {
                                RequiredPieceId = "required-failed",
                                Quantity = 1,
                                Length = 200m,
                                ProfileNumber = "P-200"
                            }
                        ],
                        LastStockLengthOptimizationResult = new StockLengthOptimizationResult
                        {
                            OptimizationGroupId = "failed",
                            Status = CutPlanStatus.Failed
                        }
                    }
                ]
            }
        };
        var report = await new StockLengthReportDataService().BuildAsync(
            new StockLengthReportDataRequest { Project = project });

        await new ClosedXmlExcelReportExporter().ExportAsync(report, filePath);

        using var workbook = new XLWorkbook(filePath);
        var summary = workbook.Worksheet("Summary");
        Assert.True(summary.Row(2).IsEmpty());
        Assert.Equal(["Empty", "Needs Generation", "Failed"], summary.Rows(6, 8).Select(row => row.Cell(5).GetString()));
        Assert.Single(summary.Tables);
        Assert.Empty(workbook.Worksheet("Cut Plans").Tables);
        Assert.Empty(workbook.Worksheet("Unplaced").Tables);
        Assert.True(workbook.Worksheet("Cut Plans").AutoFilter.IsEnabled);
        Assert.True(workbook.Worksheet("Unplaced").AutoFilter.IsEnabled);
        Assert.True(workbook.Worksheet("Cut Plans").Row(2).IsEmpty());
        Assert.True(workbook.Worksheet("Unplaced").Row(2).IsEmpty());
    }

    [Fact]
    public async Task Export_stock_length_async_preserves_natural_order_and_semantic_Stock_Group_scope()
    {
        var allPath = Path.Combine(_workspacePath, "ordered-stock-groups.xlsx");
        var filteredPath = Path.Combine(_workspacePath, "filtered-stock-group.xlsx");
        var group = CreateGroup(
            "frames", "Frames", 0, "P-10", "Clear", CutPlanStatus.Complete,
            1, "piece-10", 10m, "Lengths", 10);
        var p2Plan = CreateGroup(
            "other", "Other", 0, "P-2", "Clear", CutPlanStatus.Complete,
            1, "piece-2", 2m, "Lengths", 2)
            .LastStockLengthOptimizationResult!.CutPlans[0];
        group = group with
        {
            LastStockLengthOptimizationResult = group.LastStockLengthOptimizationResult! with
            {
                CutPlans = [group.LastStockLengthOptimizationResult.CutPlans[0], p2Plan]
            }
        };
        var project = new Project
        {
            ProjectKind = ProjectKind.StockLength,
            State = new ProjectState { OptimizationGroups = [group] }
        };
        var service = new StockLengthReportDataService();
        var exporter = new ClosedXmlExcelReportExporter();

        await exporter.ExportAsync(
            await service.BuildAsync(new StockLengthReportDataRequest { Project = project }),
            allPath);
        await exporter.ExportAsync(
            await service.BuildAsync(new StockLengthReportDataRequest
            {
                Project = project,
                Scope = new StockLengthReportScope
                {
                    OptimizationGroupId = "frames",
                    HasStockGroupFilter = true,
                    StockGroupProfileNumber = "p-10",
                    StockGroupFinish = "clear"
                }
            }),
            filteredPath);

        using var allWorkbook = new XLWorkbook(allPath);
        Assert.Equal(["P-2", "P-10"], allWorkbook.Worksheet("Summary").Rows(2, 3).Select(row => row.Cell(3).GetString()));
        using var filteredWorkbook = new XLWorkbook(filteredPath);
        var filteredSummary = filteredWorkbook.Worksheet("Summary");
        Assert.Equal("Frames", filteredSummary.Cell(2, 1).GetString());
        Assert.Equal("P-10", filteredSummary.Cell(2, 3).GetString());
        Assert.True(filteredSummary.Row(3).Cell(3).IsEmpty());
        Assert.Equal("P-10", filteredWorkbook.Worksheet("Cut Plans").Cell(2, 3).GetString());
    }

    private static OptimizationGroup CreateGroup(
        string groupId,
        string groupName,
        int order,
        string profileNumber,
        string finish,
        CutPlanStatus status,
        int stockItemNumber,
        string pieceId,
        decimal pieceLength,
        string sourceWorksheet,
        int sourceRow,
        string? unplacedPieceId = null,
        StockItemKind kind = StockItemKind.Regular)
    {
        var piece = new PieceInstance
        {
            PieceInstanceId = pieceId,
            RequiredPieceId = $"required-{pieceId}",
            InstanceNumber = 1,
            Length = pieceLength,
            ProfileNumber = profileNumber,
            Finish = finish,
            PartNumber = $"part-{pieceId}",
            PartName = $"Part {pieceId}",
            SourceReferences =
            [
                new SourceReference
                {
                    WorksheetName = sourceWorksheet,
                    WorksheetPosition = 0,
                    PhysicalRow = sourceRow,
                    SourceFingerprint = $"source-{pieceId}"
                }
            ]
        };
        var unplaced = unplacedPieceId is null
            ? Array.Empty<UnplacedPieceInstance>()
            :
            [
                new UnplacedPieceInstance
                {
                    PieceInstance = piece with
                    {
                        PieceInstanceId = unplacedPieceId,
                        RequiredPieceId = $"required-{unplacedPieceId}"
                    },
                    ReasonCode = "too-long",
                    ReasonDescription = "Piece exceeds the Stock Length."
                }
            ];

        return new OptimizationGroup
        {
            OptimizationGroupId = groupId,
            Name = groupName,
            Order = order,
            ResultStatus = OptimizationResultStatus.Valid,
            RequiredPieces =
            [
                new RequiredPiece
                {
                    RequiredPieceId = piece.RequiredPieceId,
                    Quantity = 1,
                    Length = piece.Length,
                    ProfileNumber = profileNumber,
                    Finish = finish
                }
            ],
            LastStockLengthOptimizationResult = new StockLengthOptimizationResult
            {
                OptimizationGroupId = groupId,
                Status = status,
                CutPlans =
                [
                    new CutPlan
                    {
                        CutPlanId = $"plan-{groupId}",
                        StockGroup = new StockGroup
                        {
                            ProfileNumber = profileNumber,
                            Finish = finish,
                            RequiredPieceIds = [piece.RequiredPieceId]
                        },
                        Status = status,
                        StockItems =
                        [
                            new StockItem
                            {
                                StockItemId = $"stock-{groupId}",
                                StockItemNumber = stockItemNumber,
                                Kind = kind,
                                StockLength = 120m,
                                PieceLength = pieceLength,
                                SawLoss = 0m,
                                Remainder = 120m - pieceLength,
                                UtilizationPercent = pieceLength / 120m * 100m,
                                CutSequence = [piece]
                            }
                        ],
                        UnplacedPieceInstances = unplaced
                    }
                ]
            }
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
    }
}
