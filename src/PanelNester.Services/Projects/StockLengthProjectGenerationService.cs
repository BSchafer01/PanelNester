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
            return Failure(exception.Code, exception.Message);
        }
    }

    private static ProjectOperationResult Failure(string code, string message) =>
        new()
        {
            Success = false,
            Errors = [new ValidationError(code, message)]
        };
}
