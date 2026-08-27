using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface INestingService
{
    Task<NestResponse> NestAsync(NestRequest request, CancellationToken cancellationToken = default);

    Task<NestResponse> NestAsync(
        NestRequest request,
        IProgress<NestingProgress> progress,
        CancellationToken cancellationToken = default) =>
        NestAsync(request, cancellationToken);
}
