using PanelNester.Domain.Contracts;
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

    [Fact]
    public async Task Generate_All_Stale_retains_successes_and_records_each_application_error()
    {
        var service = new StockLengthProjectGenerationService(
            new SheetOptimizerStockLengthCutPlanGenerator(new ShelfNestingService()));
        var project = ProjectWithGroups(
            Group("successful", Piece("successful-piece", 2, 40)) with
            {
                ResultStatus = OptimizationResultStatus.Stale
            },
            Group("failed", Piece("failed-piece", 1, 30)) with
            {
                StockLength = null,
                ResultStatus = OptimizationResultStatus.Stale
            },
            Group("empty"),
            Group("current", Piece("current-piece", 1, 20)) with
            {
                ResultStatus = OptimizationResultStatus.Valid,
                LastStockLengthOptimizationResult = new StockLengthOptimizationResult
                {
                    OptimizationGroupId = "current",
                    Status = CutPlanStatus.Partial,
                    Description = "Existing current Cut Plan"
                }
            });

        var generated = await service.GenerateAllStaleAsync(project);

        Assert.False(generated.Success);
        var groups = generated.Project.State.OptimizationGroups
            .ToDictionary(group => group.OptimizationGroupId);
        Assert.Equal(CutPlanStatus.Complete,
            groups["successful"].LastStockLengthOptimizationResult?.Status);
        Assert.Equal(OptimizationResultStatus.Valid, groups["successful"].ResultStatus);
        Assert.Null(groups["successful"].LastStockLengthGenerationError);
        Assert.Equal(OptimizationResultStatus.Stale, groups["failed"].ResultStatus);
        Assert.Equal("cut-plan-invalid-input", groups["failed"].LastStockLengthGenerationError?.Code);
        Assert.Equal(OptimizationResultStatus.None, groups["empty"].ResultStatus);
        Assert.Null(groups["empty"].LastStockLengthGenerationError);
        Assert.Equal("Existing current Cut Plan",
            groups["current"].LastStockLengthOptimizationResult?.Description);
        Assert.Equal(["failed"], generated.Failures.Select(failure => failure.OptimizationGroupId));
    }

    [Fact]
    public async Task Generate_All_Stale_isolates_unexpected_generator_failures()
    {
        var service = new StockLengthProjectGenerationService(new UnexpectedFailureGenerator());
        var project = ProjectWithGroups(
            Group("successful", Piece("successful-piece", 1, 20)),
            Group("failed", Piece("failed-piece", 1, 30)));

        var generated = await service.GenerateAllStaleAsync(project);

        Assert.False(generated.Success);
        var groups = generated.Project.State.OptimizationGroups
            .ToDictionary(group => group.OptimizationGroupId);
        Assert.Equal(OptimizationResultStatus.Valid, groups["successful"].ResultStatus);
        Assert.Equal("cut-plan-generation-failed", groups["failed"].LastStockLengthGenerationError?.Code);
        Assert.Equal("Unexpected generator failure.", groups["failed"].LastStockLengthGenerationError?.Message);
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

    private sealed class UnexpectedFailureGenerator : IStockLengthCutPlanGenerator
    {
        public Task<StockLengthOptimizationResult> GenerateAsync(
            StockLengthCutPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.OptimizationGroupId == "failed")
            {
                throw new InvalidOperationException("Unexpected generator failure.");
            }

            return Task.FromResult(new StockLengthOptimizationResult
            {
                OptimizationGroupId = request.OptimizationGroupId,
                Status = CutPlanStatus.Complete,
                Description = "Generated"
            });
        }
    }
}
