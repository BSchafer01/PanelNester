using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IExtrusionExcelReportExporter
{
    Task ExportAsync(
        ExtrusionReportData report,
        string filePath,
        CancellationToken cancellationToken = default);
}
