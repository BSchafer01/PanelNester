using PanelNester.Domain.Models;

namespace PanelNester.Domain.Contracts;

public interface IStockLengthExcelReportExporter
{
    Task ExportAsync(
        StockLengthReportData report,
        string filePath,
        CancellationToken cancellationToken = default);
}
