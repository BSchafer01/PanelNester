using PanelNester.Domain.Models;
using PanelNester.Services.Reporting;

namespace PanelNester.Services.Tests.Reporting;

public sealed class StockLengthReportDataServiceSpecs
{
    [Fact]
    public async Task Build_async_honors_visible_scope_and_keeps_unplaced_piece_instances()
    {
        var project = new Project
        {
            ProjectKind = ProjectKind.StockLength,
            Metadata = new ProjectMetadata { ProjectName = "Storefront Frames" },
            Settings = new ProjectSettings { InchDisplayFormat = InchDisplayFormat.Fractional16 },
            State = new ProjectState
            {
                OptimizationGroups =
                [
                    CreateGroup("north", "North", "P-100", "Clear", 120m, CutPlanStatus.Partial),
                    CreateGroup("south", "South", "P-200", "Bronze", 96m, CutPlanStatus.Complete)
                ]
            }
        };

        var report = await new StockLengthReportDataService().BuildAsync(
            new StockLengthReportDataRequest
            {
                Project = project,
                Scope = new StockLengthReportScope
                {
                    OptimizationGroupId = "north",
                    StockGroupProfileNumber = "p-100",
                    StockGroupFinish = "clear",
                    HasStockGroupFilter = true
                }
            });

        Assert.Equal("Storefront Frames", report.ProjectMetadata.ProjectName);
        Assert.Equal(InchDisplayFormat.Fractional16, report.InchDisplayFormat);
        var group = Assert.Single(report.OptimizationGroups);
        Assert.Equal("North", group.Name);
        Assert.Equal(StockLengthReportState.Partial, group.State);
        var stockGroup = Assert.Single(group.StockGroups);
        Assert.Equal("P-100", stockGroup.ProfileNumber);
        Assert.Equal(2, group.Summary.AcceptedPieceInstanceCount);
        Assert.Equal(1, group.Summary.PlacedPieceInstanceCount);
        Assert.Equal(1, stockGroup.Summary.PlacedPieceInstanceCount);
        Assert.Equal(1, stockGroup.Summary.UnplacedPieceInstanceCount);
        Assert.Equal(50m, stockGroup.Summary.UtilizationPercent);
        var stockItem = Assert.Single(stockGroup.StockItems);
        var piece = Assert.Single(stockItem.CutSequence);
        Assert.Equal("PN-10", piece.PartNumber);
        Assert.Equal("Mullion", piece.PartName);
        Assert.Equal(1, piece.Sequence);
        var placedSource = Assert.Single(piece.SourceReferences);
        Assert.Equal(("Lengths", 12), (placedSource.WorksheetName, placedSource.PhysicalRow));
        Assert.Equal(1, report.Summary.UnplacedPieceInstanceCount);
        var unplaced = Assert.Single(report.UnplacedPieceInstances);
        Assert.Equal("too-long", unplaced.ReasonCode);
        var unplacedSource = Assert.Single(unplaced.PieceInstance.SourceReferences);
        Assert.Equal(("Lengths", 13), (unplacedSource.WorksheetName, unplacedSource.PhysicalRow));
    }

    [Fact]
    public async Task Build_async_exports_empty_needs_generation_and_failed_groups()
    {
        var project = new Project
        {
            ProjectKind = ProjectKind.StockLength,
            State = new ProjectState
            {
                OptimizationGroups =
                [
                    new OptimizationGroup { OptimizationGroupId = "empty", Name = "Empty", Order = 0 },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "stale",
                        Name = "Stale",
                        Order = 1,
                        RequiredPieces = [new RequiredPiece { RequiredPieceId = "r", Quantity = 1, Length = 12m, ProfileNumber = "P" }],
                        ResultStatus = OptimizationResultStatus.Stale
                    },
                    new OptimizationGroup
                    {
                        OptimizationGroupId = "failed",
                        Name = "Failed",
                        Order = 2,
                        RequiredPieces = [new RequiredPiece { RequiredPieceId = "r2", Quantity = 1, Length = 200m, ProfileNumber = "P" }],
                        ResultStatus = OptimizationResultStatus.Valid,
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

        Assert.Equal(
            [StockLengthReportState.Empty, StockLengthReportState.NeedsGeneration, StockLengthReportState.Failed],
            report.OptimizationGroups.Select(group => group.State).ToArray());
        Assert.Equal([0, 1, 1], report.OptimizationGroups.Select(group => group.Summary.AcceptedPieceInstanceCount).ToArray());
    }

    [Fact]
    public async Task Build_async_uses_the_accepted_numeric_Stock_Group_order()
    {
        var group = CreateGroup("frames", "Frames", "P-10", "Clear", 120m, CutPlanStatus.Complete);
        var secondPlan = CreateGroup("other", "Other", "P-2", "Clear", 120m, CutPlanStatus.Complete)
            .LastStockLengthOptimizationResult!.CutPlans[0];
        group = group with
        {
            LastStockLengthOptimizationResult = group.LastStockLengthOptimizationResult! with
            {
                CutPlans = [group.LastStockLengthOptimizationResult.CutPlans[0], secondPlan]
            }
        };
        var project = new Project
        {
            ProjectKind = ProjectKind.StockLength,
            State = new ProjectState { OptimizationGroups = [group] }
        };

        var report = await new StockLengthReportDataService().BuildAsync(
            new StockLengthReportDataRequest { Project = project });

        Assert.Equal(["P-2", "P-10"], Assert.Single(report.OptimizationGroups).StockGroups.Select(item => item.ProfileNumber));
    }

    private static OptimizationGroup CreateGroup(
        string id,
        string name,
        string profile,
        string finish,
        decimal stockLength,
        CutPlanStatus status) =>
        new()
        {
            OptimizationGroupId = id,
            Name = name,
            ResultStatus = OptimizationResultStatus.Valid,
            RequiredPieces = [new RequiredPiece { RequiredPieceId = $"{id}-required", Quantity = 2, Length = stockLength / 2m, ProfileNumber = profile, Finish = finish }],
            LastStockLengthOptimizationResult = new StockLengthOptimizationResult
            {
                OptimizationGroupId = id,
                Status = status,
                CutPlans =
                [
                    new CutPlan
                    {
                        CutPlanId = $"{id}-plan",
                        Status = status,
                        StockGroup = new StockGroup { ProfileNumber = profile, Finish = finish },
                        StockItems =
                        [
                            new StockItem
                            {
                                StockItemId = $"{id}-stock-1",
                                StockItemNumber = 1,
                                StockLength = stockLength,
                                PieceLength = stockLength / 2m,
                                SawLoss = 0m,
                                Remainder = stockLength / 2m,
                                UtilizationPercent = 50m,
                                CutSequence =
                                [
                                    new PieceInstance
                                    {
                                        PieceInstanceId = $"{id}-piece-1",
                                        InstanceNumber = 1,
                                        Length = stockLength / 2m,
                                        ProfileNumber = profile,
                                        Finish = finish,
                                        PartNumber = "PN-10",
                                        PartName = "Mullion",
                                        SourceReferences = [new SourceReference { WorksheetName = "Lengths", PhysicalRow = 12 }]
                                    }
                                ]
                            }
                        ],
                        UnplacedPieceInstances =
                        [
                            new UnplacedPieceInstance
                            {
                                PieceInstance = new PieceInstance
                                {
                                    PieceInstanceId = $"{id}-piece-2",
                                    InstanceNumber = 2,
                                    Length = stockLength + 1m,
                                    ProfileNumber = profile,
                                    Finish = finish,
                                    PartNumber = "PN-11",
                                    SourceReferences = [new SourceReference { WorksheetName = "Lengths", PhysicalRow = 13 }]
                                },
                                ReasonCode = "too-long",
                                ReasonDescription = "Piece exceeds Stock Length."
                            }
                        ]
                    }
                ]
            }
        };
}
