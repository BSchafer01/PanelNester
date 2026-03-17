using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IReportDataService
{
    Task<ReportData> BuildReportDataAsync(ReportDataRequest request, CancellationToken cancellationToken = default);
}
