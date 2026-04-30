using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IExtrusionTakeoffService
{
    Task<ExtrusionLayoutState> BuildLayoutAsync(
        ExtrusionLayoutRequest request,
        CancellationToken cancellationToken = default);

    Task<ExtrusionReportData> BuildReportAsync(
        ExtrusionReportRequest request,
        CancellationToken cancellationToken = default);
}
