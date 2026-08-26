using PanelNester.Domain.Models;
using PanelNester.Services.Nesting;
using PanelNester.Services.Projects;

namespace PanelNester.Services.Tests.Projects;

public sealed class StockLengthProjectGenerationSpecs
{
    [Fact]
    public async Task Generate_Selected_persists_the_current_Cut_Plan_only_on_the_selected_group()
    {
        var service = new StockLengthProjectGenerationService(
            new SheetOptimizerStockLengthCutPlanGenerator(new ShelfNestingService()));
        var project = ProjectWithGroups(
            Group("selected", Piece("selected-piece", 2, 40)),
            Group("other", Piece("other-piece", 1, 30)));

        var generated = await service.GenerateSelectedAsync(project, "selected");

        Assert.True(generated.Success);
        var groups = generated.Project!.State.OptimizationGroups.ToDictionary(group => group.OptimizationGroupId);
        Assert.Equal(OptimizationResultStatus.Valid, groups["selected"].ResultStatus);
        Assert.Equal(CutPlanStatus.Complete, groups["selected"].LastStockLengthOptimizationResult?.Status);
        Assert.Null(groups["other"].LastStockLengthOptimizationResult);
    }

    [Fact]
    public async Task Empty_Optimization_Groups_cannot_generate_and_remain_Empty()
    {
        var service = new StockLengthProjectGenerationService(
            new SheetOptimizerStockLengthCutPlanGenerator(new ShelfNestingService()));
        var project = ProjectWithGroups(Group("empty"));

        var generated = await service.GenerateSelectedAsync(project, "empty");

        Assert.False(generated.Success);
        Assert.Equal("cut-plan-empty-group", Assert.Single(generated.Errors).Code);
        Assert.Equal(OptimizationResultStatus.None, project.State.OptimizationGroups[0].ResultStatus);
    }

    private static Project ProjectWithGroups(params OptimizationGroup[] groups) =>
        new()
        {
            ProjectId = "stock-project",
            ProjectKind = ProjectKind.StockLength,
            Settings = new ProjectSettings { KerfWidth = 0.125m },
            State = new ProjectState { OptimizationGroups = groups }
        };

    private static OptimizationGroup Group(string id, params RequiredPiece[] pieces) =>
        new()
        {
            OptimizationGroupId = id,
            Name = id,
            StockLength = 120,
            RequiredPieces = pieces
        };

    private static RequiredPiece Piece(string id, int quantity, decimal length) =>
        new()
        {
            RequiredPieceId = id,
            Quantity = quantity,
            Length = length,
            ProfileNumber = "P-100"
        };
}
