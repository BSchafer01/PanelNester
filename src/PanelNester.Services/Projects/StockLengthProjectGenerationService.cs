using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using PanelNester.Services;

namespace PanelNester.Services.Projects;

public sealed class StockLengthProjectGenerationService(IStockLengthCutPlanGenerator generator)
    : IStockLengthProjectGenerationService
{
    private readonly IStockLengthCutPlanGenerator _generator =
        generator ?? throw new ArgumentNullException(nameof(generator));

    public async Task<ProjectOperationResult> GenerateSelectedAsync(
        Project project,
        string optimizationGroupId,
        CancellationToken cancellationToken = default) =>
        await GenerateSelectedCoreAsync(project, optimizationGroupId, null, cancellationToken)
            .ConfigureAwait(false);

    public async Task<ProjectOperationResult> GenerateSelectedAsync(
        Project project,
        string optimizationGroupId,
        IProgress<StockLengthGenerationProgress> progress,
        CancellationToken cancellationToken = default) =>
        await GenerateSelectedCoreAsync(project, optimizationGroupId, progress, cancellationToken)
            .ConfigureAwait(false);

    private async Task<ProjectOperationResult> GenerateSelectedCoreAsync(
        Project project,
        string optimizationGroupId,
        IProgress<StockLengthGenerationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.ProjectKind != ProjectKind.StockLength)
        {
            return Failure("cut-plan-project-kind-invalid", "Only Stock-Length Projects can generate Cut Plans.");
        }

        var groups = project.State.OptimizationGroups.ToArray();
        var groupIndex = Array.FindIndex(groups, group =>
            string.Equals(group.OptimizationGroupId, optimizationGroupId, StringComparison.Ordinal));
        if (groupIndex < 0)
        {
            return Failure("optimization-group-not-found", "The Optimization Group was not found.");
        }

        var group = groups[groupIndex];
        if (group.RequiredPieces.Count == 0)
        {
            return Failure("cut-plan-empty-group", "Empty Optimization Groups cannot generate a Cut Plan.");
        }

        try
        {
            Report(progress, 0, 1, group, $"Generating Cut Plan for '{group.Name}'");
            var request = new StockLengthCutPlanRequest
            {
                OptimizationGroupId = group.OptimizationGroupId,
                RequiredPieces = group.RequiredPieces,
                StockLength = group.StockLength ?? 0m,
                SawKerf = project.Settings.KerfWidth
            };
            var result = await GenerateGroupAsync(request, progress, 0, 1, cancellationToken)
                .ConfigureAwait(false);
            EnsureCommitReady(group, result);
            Report(progress, 1, 1, group, $"Generated Cut Plan for '{group.Name}'");
            groups[groupIndex] = group with
            {
                LastStockLengthOptimizationResult = result,
                LastStockLengthGenerationError = null,
                ResultStatus = OptimizationResultStatus.Valid
            };
            return new ProjectOperationResult
            {
                Success = true,
                Project = project with
                {
                    State = project.State with { OptimizationGroups = groups }
                }
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CutPlanGenerationException exception)
        {
            return GenerationFailure(project, groups, groupIndex, group, exception.Code, exception.Message);
        }
        catch (OutOfMemoryException exception)
        {
            return GenerationFailure(
                project,
                groups,
                groupIndex,
                group,
                "cut-plan-resource-exhausted",
                $"Cut Plan generation ran out of available memory. {exception.Message}");
        }
        catch (Exception exception)
        {
            return GenerationFailure(
                project,
                groups,
                groupIndex,
                group,
                "cut-plan-generation-failed",
                exception.Message);
        }
    }

    public async Task<StockLengthProjectGenerationResult> GenerateAllStaleAsync(
        Project project,
        CancellationToken cancellationToken = default) =>
        await GenerateAllStaleCoreAsync(project, null, cancellationToken).ConfigureAwait(false);

    public async Task<StockLengthProjectGenerationResult> GenerateAllStaleAsync(
        Project project,
        IProgress<StockLengthGenerationProgress> progress,
        CancellationToken cancellationToken = default) =>
        await GenerateAllStaleCoreAsync(project, progress, cancellationToken).ConfigureAwait(false);

    private async Task<StockLengthProjectGenerationResult> GenerateAllStaleCoreAsync(
        Project project,
        IProgress<StockLengthGenerationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.ProjectKind != ProjectKind.StockLength)
        {
            return new StockLengthProjectGenerationResult
            {
                Project = project,
                Failures =
                [
                    new StockLengthGenerationFailure
                    {
                        Code = "cut-plan-project-kind-invalid",
                        Message = "Only Stock-Length Projects can generate Cut Plans."
                    }
                ]
            };
        }

        var groups = project.State.OptimizationGroups
            .OrderBy(group => group.Order)
            .ToArray();
        var groupsToGenerate = groups
            .Where(group => group.RequiredPieces.Count > 0 && group.ResultStatus != OptimizationResultStatus.Valid)
            .Select(group => group.OptimizationGroupId)
            .ToHashSet(StringComparer.Ordinal);
        var completedGroups = 0;
        var failures = new List<StockLengthGenerationFailure>();
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var group = groups[groupIndex];
            if (!groupsToGenerate.Contains(group.OptimizationGroupId))
            {
                continue;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(
                    progress,
                    completedGroups,
                    groupsToGenerate.Count,
                    group,
                    $"Generating Cut Plan for '{group.Name}'");
                var request = new StockLengthCutPlanRequest
                {
                    OptimizationGroupId = group.OptimizationGroupId,
                    RequiredPieces = group.RequiredPieces,
                    StockLength = group.StockLength ?? 0m,
                    SawKerf = project.Settings.KerfWidth
                };
                var result = await GenerateGroupAsync(
                        request,
                        progress,
                        completedGroups,
                        groupsToGenerate.Count,
                        cancellationToken)
                    .ConfigureAwait(false);
                EnsureCommitReady(group, result);
                groups[groupIndex] = group with
                {
                    LastStockLengthOptimizationResult = result,
                    LastStockLengthGenerationError = null,
                    ResultStatus = OptimizationResultStatus.Valid
                };
                completedGroups++;
                Report(
                    progress,
                    completedGroups,
                    groupsToGenerate.Count,
                    group,
                    $"Generated Cut Plan for '{group.Name}'");
            }
            catch (OperationCanceledException)
            {
                failures.Add(new StockLengthGenerationFailure
                {
                    OptimizationGroupId = group.OptimizationGroupId,
                    Code = "cut-plan-generation-cancelled",
                    Message = "Cut Plan generation was cancelled."
                });
                break;
            }
            catch (CutPlanGenerationException exception)
            {
                var failure = new StockLengthGenerationFailure
                {
                    OptimizationGroupId = group.OptimizationGroupId,
                    Code = exception.Code,
                    Message = exception.Message
                };
                failures.Add(failure);
                groups[groupIndex] = group with
                {
                    LastStockLengthGenerationError = new ValidationError(
                        failure.Code,
                        failure.Message)
                };
            }
            catch (Exception exception)
            {
                var code = exception is OutOfMemoryException
                    ? "cut-plan-resource-exhausted"
                    : "cut-plan-generation-failed";
                var message = exception is OutOfMemoryException
                    ? $"Cut Plan generation ran out of available memory. {exception.Message}"
                    : exception.Message;
                var failure = new StockLengthGenerationFailure
                {
                    OptimizationGroupId = group.OptimizationGroupId,
                    Code = code,
                    Message = message
                };
                failures.Add(failure);
                groups[groupIndex] = group with
                {
                    LastStockLengthGenerationError = new ValidationError(
                        failure.Code,
                        failure.Message)
                };
            }
        }

        return new StockLengthProjectGenerationResult
        {
            Project = project with
            {
                State = project.State with { OptimizationGroups = groups }
            },
            Failures = failures
        };
    }

    private static ProjectOperationResult Failure(string code, string message) =>
        new()
        {
            Success = false,
            Errors = [new ValidationError(code, message)]
        };

    private static ProjectOperationResult GenerationFailure(
        Project project,
        OptimizationGroup[] groups,
        int groupIndex,
        OptimizationGroup group,
        string code,
        string message)
    {
        groups[groupIndex] = group with
        {
            LastStockLengthGenerationError = new ValidationError(code, message)
        };
        return new ProjectOperationResult
        {
            Success = false,
            Project = project with
            {
                State = project.State with { OptimizationGroups = groups }
            },
            Errors = [new ValidationError(code, message)]
        };
    }

    private static void Report(
        IProgress<StockLengthGenerationProgress>? progress,
        int completed,
        int total,
        OptimizationGroup group,
        string label) =>
        progress?.Report(new StockLengthGenerationProgress
        {
            Phase = StockLengthGenerationProgressPhase.OptimizationGroups,
            CompletedOptimizationGroups = completed,
            TotalOptimizationGroups = total,
            OptimizationGroupId = group.OptimizationGroupId,
            Label = label
        });

    private static void EnsureCommitReady(
        OptimizationGroup group,
        StockLengthOptimizationResult result)
    {
        if (!string.Equals(
                group.OptimizationGroupId,
                result.OptimizationGroupId,
                StringComparison.Ordinal))
        {
            throw new CutPlanGenerationException(
                "cut-plan-result-invalid",
                "Generated Cut Plan belongs to a different Optimization Group.");
        }

        if (result.Status == CutPlanStatus.Failed)
        {
            throw new CutPlanGenerationException(
                "cut-plan-no-pieces-placed",
                "Cut Plan generation did not place any Piece Instances; the current result was retained.");
        }
    }

    private Task<StockLengthOptimizationResult> GenerateGroupAsync(
        StockLengthCutPlanRequest request,
        IProgress<StockLengthGenerationProgress>? progress,
        int completedOptimizationGroups,
        int totalOptimizationGroups,
        CancellationToken cancellationToken)
    {
        if (progress is null)
        {
            return _generator.GenerateAsync(request, cancellationToken);
        }

        return _generator.GenerateAsync(
            request,
            new SynchronousProgress<StockLengthGenerationProgress>(report => progress.Report(report with
            {
                CompletedOptimizationGroups = completedOptimizationGroups,
                TotalOptimizationGroups = totalOptimizationGroups
            })),
            cancellationToken);
    }

}
