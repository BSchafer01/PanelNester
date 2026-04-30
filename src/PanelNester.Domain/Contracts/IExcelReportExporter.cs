using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IExcelReportExporter
{
    Task ExportAsync(
        ReportData report,
        string filePath,
        CancellationToken cancellationToken = default);
}
