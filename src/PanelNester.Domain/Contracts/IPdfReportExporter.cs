using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IPdfReportExporter
{
    Task ExportAsync(
        ReportData report,
        string filePath,
        CancellationToken cancellationToken = default);
}
