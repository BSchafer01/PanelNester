using PanelNester.Domain.Models;
using PanelNester.Services.Projects;

namespace PanelNester.Services.Tests.Projects;

public sealed class OversizedStockAssignmentServiceSpecs
{
    [Fact]
    public async Task Assign_places_each_fitting_overlong_instance_on_its_own_oversized_Stock_Item()
    {
        var project = ProjectWithResult(
            Unplaced("fits", 130m),
            Unplaced("also-fits", 144m),
            Unplaced("still-too-long", 170m));

        var assigned = await new OversizedStockAssignmentService()
            .SetAsync(project, "frames", "144");

        Assert.True(assigned.Success);
        var result = Assert.Single(assigned.Project!.State.OptimizationGroups)
            .LastStockLengthOptimizationResult!;
        Assert.Equal(144m, result.OversizedStockLength);
        var plan = Assert.Single(result.CutPlans);
        var oversized = plan.StockItems
            .Where(item => item.Kind == StockItemKind.Oversized)
            .ToDictionary(item => Assert.Single(item.CutSequence).PieceInstanceId);
        Assert.Equal(2, oversized.Count);
        Assert.Equal(144m, oversized["fits"].StockLength);
        Assert.Equal(130m, oversized["fits"].PieceLength);
        Assert.Equal(0m, oversized["fits"].SawLoss);
        Assert.Equal(14m, oversized["fits"].Remainder);
        Assert.Equal(144m, oversized["also-fits"].PieceLength);
        Assert.Equal("still-too-long", Assert.Single(plan.UnplacedPieceInstances).PieceInstance.PieceInstanceId);
        Assert.Equal(CutPlanStatus.Partial, plan.Status);
    }

    [Fact]
    public async Task Change_rebuilds_and_remove_restores_every_overlong_instance_to_Unplaced()
    {
        var service = new OversizedStockAssignmentService();
        var project = ProjectWithResult(Unplaced("shorter", 130m), Unplaced("longer", 145m));

        var assigned = await service.SetAsync(project, "frames", "150");
        var complete = Assert.Single(assigned.Project!.State.OptimizationGroups).LastStockLengthOptimizationResult!;
        Assert.Equal(CutPlanStatus.Complete, complete.Status);
        Assert.Equal([1, 2], Assert.Single(complete.CutPlans).StockItems.Select(item => item.StockItemNumber));

        var changed = await service.SetAsync(assigned.Project, "frames", "140");
        var partial = Assert.Single(changed.Project!.State.OptimizationGroups).LastStockLengthOptimizationResult!;
        Assert.Equal(140m, partial.OversizedStockLength);
        Assert.Equal("shorter", Assert.Single(Assert.Single(partial.CutPlans).StockItems).CutSequence.Single().PieceInstanceId);
        Assert.Equal("longer", Assert.Single(Assert.Single(partial.CutPlans).UnplacedPieceInstances).PieceInstance.PieceInstanceId);

        var removed = await service.SetAsync(changed.Project, "frames", null);
        var restored = Assert.Single(removed.Project!.State.OptimizationGroups).LastStockLengthOptimizationResult!;
        Assert.Null(restored.OversizedStockLength);
        var restoredPlan = Assert.Single(restored.CutPlans);
        Assert.Empty(restoredPlan.StockItems);
        Assert.Equal(["longer", "shorter"], restoredPlan.UnplacedPieceInstances.Select(item => item.PieceInstance.PieceInstanceId).Order());
        Assert.Equal(CutPlanStatus.Failed, restored.Status);
    }

    [Theory]
    [InlineData("120", "oversized-stock-length-invalid")]
    [InlineData("129", "oversized-stock-length-too-short")]
    public async Task Assign_rejects_lengths_that_are_not_larger_and_useful(string length, string expectedCode)
    {
        var result = await new OversizedStockAssignmentService()
            .SetAsync(ProjectWithResult(Unplaced("piece", 130m)), "frames", length);

        Assert.False(result.Success);
        Assert.Equal(expectedCode, Assert.Single(result.Errors).Code);
    }

    private static Project ProjectWithResult(params UnplacedPieceInstance[] unplaced) => new()
    {
        ProjectId = "stock-project",
        ProjectKind = ProjectKind.StockLength,
        State = new ProjectState
        {
            OptimizationGroups =
            [
                new OptimizationGroup
                {
                    OptimizationGroupId = "frames",
                    Name = "Frames",
                    StockLength = 120m,
                    RequiredPieces = unplaced.Select(item => new RequiredPiece
                    {
                        RequiredPieceId = item.PieceInstance.RequiredPieceId,
                        Quantity = 1,
                        Length = item.PieceInstance.Length,
                        ProfileNumber = item.PieceInstance.ProfileNumber
                    }).ToArray(),
                    ResultStatus = OptimizationResultStatus.Valid,
                    LastStockLengthOptimizationResult = new StockLengthOptimizationResult
                    {
                        OptimizationGroupId = "frames",
                        Status = CutPlanStatus.Failed,
                        CutPlans =
                        [
                            new CutPlan
                            {
                                CutPlanId = "frames:P-100",
                                StockGroup = new StockGroup
                                {
                                    ProfileNumber = "P-100",
                                    RequiredPieceIds = unplaced.Select(item => item.PieceInstance.RequiredPieceId).ToArray()
                                },
                                Status = CutPlanStatus.Failed,
                                UnplacedPieceInstances = unplaced
                            }
                        ]
                    }
                }
            ]
        }
    };

    private static UnplacedPieceInstance Unplaced(string id, decimal length) => new()
    {
        PieceInstance = new PieceInstance
        {
            PieceInstanceId = id,
            RequiredPieceId = id,
            InstanceNumber = 1,
            Length = length,
            ProfileNumber = "P-100"
        },
        ReasonCode = "exceeds-stock-length",
        ReasonDescription = "Piece Instance exceeds Stock Length."
    };
}
