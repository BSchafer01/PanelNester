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

        using var stream = ImportFileAccessGuard.OpenReadShared(workbookPath);
        ImportFileAccessGuard.RejectEncryptedOpenXmlPackage(stream);
        using var workbook = new XLWorkbook(stream);
        var worksheets = new List<ImportWorksheetDescriptor>();
        foreach (var worksheet in workbook.Worksheets.OrderBy(worksheet => worksheet.Position))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (worksheet.Visibility == XLWorksheetVisibility.Visible &&
                worksheet.RangeUsed() is not null)
            {
                worksheets.Add(HeadingRangeDetector.Describe(worksheet));
            }
        }

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
