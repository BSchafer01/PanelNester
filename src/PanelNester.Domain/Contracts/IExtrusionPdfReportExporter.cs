using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IExtrusionPdfReportExporter
{
    Task ExportAsync(
        ExtrusionReportData report,
        string filePath,
        CancellationToken cancellationToken = default);
}
