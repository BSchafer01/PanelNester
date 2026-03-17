using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface INestingService
{
    Task<NestResponse> NestAsync(NestRequest request, CancellationToken cancellationToken = default);
}
