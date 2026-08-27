using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Projects;

public sealed class StockLengthProjectGenerationService(IStockLengthCutPlanGenerator generator)
    : IStockLengthProjectGenerationService
{
    private readonly IStockLengthCutPlanGenerator _generator =
        generator ?? throw new ArgumentNullException(nameof(generator));

    public async Task<ProjectOperationResult> GenerateSelectedAsync(
        Project project,
        string optimizationGroupId,
        CancellationToken cancellationToken = default)
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
            var result = await _generator.GenerateAsync(new StockLengthCutPlanRequest
            {
                OptimizationGroupId = group.OptimizationGroupId,
                RequiredPieces = group.RequiredPieces,
                StockLength = group.StockLength ?? 0m,
                SawKerf = project.Settings.KerfWidth
            }, cancellationToken).ConfigureAwait(false);
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
            groups[groupIndex] = group with
            {
                LastStockLengthGenerationError = new ValidationError(exception.Code, exception.Message)
            };
            return new ProjectOperationResult
            {
                Success = false,
                Project = project with
                {
                    State = project.State with { OptimizationGroups = groups }
                },
                Errors = [new ValidationError(exception.Code, exception.Message)]
            };
        }
    }

    public async Task<StockLengthProjectGenerationResult> GenerateAllStaleAsync(
        Project project,
        CancellationToken cancellationToken = default)
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
        var failures = new List<StockLengthGenerationFailure>();
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = groups[groupIndex];
            if (group.RequiredPieces.Count == 0 || group.ResultStatus == OptimizationResultStatus.Valid)
            {
                continue;
            }

            try
            {
                var result = await _generator.GenerateAsync(new StockLengthCutPlanRequest
                {
                    OptimizationGroupId = group.OptimizationGroupId,
                    RequiredPieces = group.RequiredPieces,
                    StockLength = group.StockLength ?? 0m,
                    SawKerf = project.Settings.KerfWidth
                }, cancellationToken).ConfigureAwait(false);
                groups[groupIndex] = group with
                {
                    LastStockLengthOptimizationResult = result,
                    LastStockLengthGenerationError = null,
                    ResultStatus = OptimizationResultStatus.Valid
                };
            }
            catch (OperationCanceledException)
            {
                throw;
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
}
