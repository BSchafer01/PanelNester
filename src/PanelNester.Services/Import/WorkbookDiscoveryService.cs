using ClosedXML.Excel;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

public sealed class WorkbookDiscoveryService
{
    public Task<WorkbookDiscovery> DiscoverAsync(
        string workbookPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        cancellationToken.ThrowIfCancellationRequested();

        using var workbook = new XLWorkbook(workbookPath);
        var worksheets = workbook.Worksheets
            .Where(worksheet =>
                worksheet.Visibility == XLWorksheetVisibility.Visible &&
                worksheet.RangeUsed() is not null)
            .OrderBy(worksheet => worksheet.Position)
            .Select(worksheet => new ImportWorksheetDescriptor
            {
                WorksheetName = worksheet.Name,
                OriginalPosition = worksheet.Position
            })
            .ToArray();

        return Task.FromResult(new WorkbookDiscovery
        {
            InitialWorksheetName = worksheets.FirstOrDefault()?.WorksheetName ?? string.Empty,
            Worksheets = worksheets,
            MacrosPresent = string.Equals(
                Path.GetExtension(workbookPath),
                ".xlsm",
                StringComparison.OrdinalIgnoreCase)
        });
    }
}
