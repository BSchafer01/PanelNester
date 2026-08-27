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

    [Fact]
    public async Task Generate_Selected_reports_resource_exhaustion_without_replacing_the_current_result()
    {
        var service = new StockLengthProjectGenerationService(new ResourceExhaustedGenerator());
        var current = new StockLengthOptimizationResult
        {
            OptimizationGroupId = "frames",
            Status = CutPlanStatus.Complete,
            Description = "Current finalized result"
        };
        var project = ProjectWithGroups(Group("frames", Piece("piece", 20_001, 20)) with
        {
            ResultStatus = OptimizationResultStatus.Valid,
            LastStockLengthOptimizationResult = current
        });

        var generated = await service.GenerateSelectedAsync(project, "frames");

        Assert.False(generated.Success);
        var group = Assert.Single(generated.Project!.State.OptimizationGroups);
        Assert.Same(current, group.LastStockLengthOptimizationResult);
        Assert.Equal(OptimizationResultStatus.Valid, group.ResultStatus);
        Assert.Equal("cut-plan-resource-exhausted", Assert.Single(generated.Errors).Code);
    }

    [Fact]
    public async Task Generate_Selected_does_not_replace_the_current_result_with_a_Failed_Cut_Plan()
    {
        var service = new StockLengthProjectGenerationService(new FailedResultGenerator());
        var current = new StockLengthOptimizationResult
        {
            OptimizationGroupId = "frames",
            Status = CutPlanStatus.Partial,
            Description = "Current finalized result"
        };
        var project = ProjectWithGroups(Group("frames", Piece("piece", 1, 20)) with
        {
            ResultStatus = OptimizationResultStatus.Valid,
            LastStockLengthOptimizationResult = current
        });

        var generated = await service.GenerateSelectedAsync(project, "frames");

        Assert.False(generated.Success);
        var group = Assert.Single(generated.Project!.State.OptimizationGroups);
        Assert.Same(current, group.LastStockLengthOptimizationResult);
        Assert.Equal(OptimizationResultStatus.Valid, group.ResultStatus);
        Assert.Equal("cut-plan-no-pieces-placed", Assert.Single(generated.Errors).Code);
    }

    [Fact]
    public async Task Generate_All_Needs_Generation_returns_completed_groups_when_a_later_group_is_cancelled()
    {
        var service = new StockLengthProjectGenerationService(new CancelSecondGroupGenerator());
        var project = ProjectWithGroups(
            Group("first", Piece("first-piece", 1, 20)) with { ResultStatus = OptimizationResultStatus.Stale },
            Group("second", Piece("second-piece", 1, 20)) with { ResultStatus = OptimizationResultStatus.Stale },
            Group("third", Piece("third-piece", 1, 20)) with { ResultStatus = OptimizationResultStatus.Stale });

        var generated = await service.GenerateAllStaleAsync(project);

        Assert.False(generated.Success);
        var groups = generated.Project.State.OptimizationGroups.ToDictionary(group => group.OptimizationGroupId);
        Assert.Equal(OptimizationResultStatus.Valid, groups["first"].ResultStatus);
        Assert.Equal(OptimizationResultStatus.Stale, groups["second"].ResultStatus);
        Assert.Equal(OptimizationResultStatus.Stale, groups["third"].ResultStatus);
        var failure = Assert.Single(generated.Failures);
        Assert.Equal("second", failure.OptimizationGroupId);
        Assert.Equal("cut-plan-generation-cancelled", failure.Code);
    }

    [Fact]
    public async Task Generate_All_Needs_Generation_reports_Optimization_Group_progress_through_a_controllable_seam()
    {
        var service = new StockLengthProjectGenerationService(new SuccessfulGenerator());
        var reports = new List<StockLengthGenerationProgress>();
        var project = ProjectWithGroups(
            Group("first", Piece("first-piece", 1, 20)),
            Group("second", Piece("second-piece", 1, 20)));

        await service.GenerateAllStaleAsync(project, new InlineProgress(reports.Add));

        Assert.Collection(
            reports,
            report => Assert.Equal((0, 2, "first"), (report.CompletedOptimizationGroups, report.TotalOptimizationGroups, report.OptimizationGroupId)),
            report => Assert.Equal((1, 2, "first"), (report.CompletedOptimizationGroups, report.TotalOptimizationGroups, report.OptimizationGroupId)),
            report => Assert.Equal((1, 2, "second"), (report.CompletedOptimizationGroups, report.TotalOptimizationGroups, report.OptimizationGroupId)),
            report => Assert.Equal((2, 2, "second"), (report.CompletedOptimizationGroups, report.TotalOptimizationGroups, report.OptimizationGroupId)));
        Assert.All(reports, report => Assert.DoesNotContain("__stock__", report.Label, StringComparison.Ordinal));
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

    private sealed class ResourceExhaustedGenerator : IStockLengthCutPlanGenerator
    {
        public Task<StockLengthOptimizationResult> GenerateAsync(
            StockLengthCutPlanRequest request,
            CancellationToken cancellationToken = default) =>
            throw new OutOfMemoryException("Not enough memory for the requested quantity.");
    }

    private sealed class FailedResultGenerator : IStockLengthCutPlanGenerator
    {
        public Task<StockLengthOptimizationResult> GenerateAsync(
            StockLengthCutPlanRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new StockLengthOptimizationResult
            {
                OptimizationGroupId = request.OptimizationGroupId,
                Status = CutPlanStatus.Failed,
                Description = "Nothing placed"
            });
    }

    private sealed class CancelSecondGroupGenerator : IStockLengthCutPlanGenerator
    {
        private int _calls;

        public Task<StockLengthOptimizationResult> GenerateAsync(
            StockLengthCutPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 2)
            {
                throw new OperationCanceledException("Cancelled by the user.");
            }

            return Task.FromResult(Result(request));
        }
    }

    private sealed class SuccessfulGenerator : IStockLengthCutPlanGenerator
    {
        public Task<StockLengthOptimizationResult> GenerateAsync(
            StockLengthCutPlanRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result(request));
    }

    private sealed class InlineProgress(Action<StockLengthGenerationProgress> report)
        : IProgress<StockLengthGenerationProgress>
    {
        public void Report(StockLengthGenerationProgress value) => report(value);
    }

    private static StockLengthOptimizationResult Result(StockLengthCutPlanRequest request) =>
        new()
        {
            OptimizationGroupId = request.OptimizationGroupId,
            Status = CutPlanStatus.Complete,
            Description = "Generated"
        };
}
