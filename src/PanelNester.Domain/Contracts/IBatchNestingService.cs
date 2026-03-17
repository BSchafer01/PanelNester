using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IBatchNestingService
{
    Task<BatchNestResponse> NestBatchAsync(BatchNestRequest request, CancellationToken cancellationToken = default);
}
