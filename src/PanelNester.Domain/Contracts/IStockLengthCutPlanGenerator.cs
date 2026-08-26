using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IStockLengthCutPlanGenerator
{
    Task<StockLengthOptimizationResult> GenerateAsync(
        StockLengthCutPlanRequest request,
        CancellationToken cancellationToken = default);
}
