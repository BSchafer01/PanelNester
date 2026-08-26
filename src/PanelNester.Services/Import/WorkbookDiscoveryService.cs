using ClosedXML.Excel;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

public sealed class WorkbookDiscoveryService
{
    private readonly IProgress<WorkbookImportProgress>? _progress;

    public WorkbookDiscoveryService(IProgress<WorkbookImportProgress>? progress = null)
    {
        _progress = progress;
    }

    public Task<WorkbookDiscovery> DiscoverAsync(
        string workbookPath,
        CancellationToken cancellationToken = default) =>
        DiscoverAsync(workbookPath, ProjectKind.Sheet, cancellationToken);

    public Task<WorkbookDiscovery> DiscoverAsync(
        string workbookPath,
        ProjectKind projectKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = ImportFileAccessGuard.OpenReadShared(workbookPath);
        ImportFileAccessGuard.RejectEncryptedOpenXmlPackage(stream);
        using var workbook = new XLWorkbook(stream);
        var worksheets = new List<ImportWorksheetDescriptor>();
        var orderedWorksheets = workbook.Worksheets.OrderBy(worksheet => worksheet.Position).ToArray();
        for (var worksheetIndex = 0; worksheetIndex < orderedWorksheets.Length; worksheetIndex++)
        {
            var worksheet = orderedWorksheets[worksheetIndex];
            _progress?.Report(new WorkbookImportProgress
            {
                Phase = WorkbookImportPhase.InspectingWorksheets,
                Label = "Inspecting Worksheets",
                Current = worksheetIndex + 1,
                Total = orderedWorksheets.Length,
                WorksheetName = worksheet.Name
            });
            cancellationToken.ThrowIfCancellationRequested();
            if (worksheet.Visibility == XLWorksheetVisibility.Visible &&
                worksheet.RangeUsed() is not null)
            {
                worksheets.Add(HeadingRangeDetector.Describe(worksheet, projectKind));
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
