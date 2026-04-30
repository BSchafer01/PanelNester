using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IStiffenerPdfReportExporter
{
    Task ExportAsync(
        StiffenerTakeoffReportData report,
        string filePath,
        CancellationToken cancellationToken = default);
}
