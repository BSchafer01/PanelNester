using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IStockLengthProjectGenerationService
{
    Task<ProjectOperationResult> GenerateSelectedAsync(
        Project project,
        string optimizationGroupId,
        CancellationToken cancellationToken = default);

    Task<ProjectOperationResult> GenerateSelectedAsync(
        Project project,
        string optimizationGroupId,
        IProgress<StockLengthGenerationProgress> progress,
        CancellationToken cancellationToken = default);

    Task<StockLengthProjectGenerationResult> GenerateAllStaleAsync(
        Project project,
        CancellationToken cancellationToken = default);

    Task<StockLengthProjectGenerationResult> GenerateAllStaleAsync(
        Project project,
        IProgress<StockLengthGenerationProgress> progress,
        CancellationToken cancellationToken = default);
}
