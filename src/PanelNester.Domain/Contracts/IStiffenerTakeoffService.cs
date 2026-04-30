using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IStiffenerTakeoffService
{
    Task<StiffenerTakeoffReportData> BuildAsync(
        StiffenerTakeoffRequest request,
        CancellationToken cancellationToken = default);
}
