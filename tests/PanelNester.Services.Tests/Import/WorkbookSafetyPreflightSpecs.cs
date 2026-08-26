using ClosedXML.Excel;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;

namespace PanelNester.Services.Tests.Import;

public sealed class WorkbookSafetyPreflightSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(
        Path.GetTempPath(),
        $"PanelNester.WorkbookSafety.{Guid.NewGuid():N}");

    [Fact]
    public async Task Import_measures_package_characteristics_before_reading_Worksheet_cells()
    {
        var workbookPath = CreateWorkbook();
        var progress = new RecordingProgress();
        var service = new XlsxImportService(
            DemoMaterialCatalog.All,
            progress: progress);

        var response = await service.ImportAsync(new ImportRequest
        {
            FilePath = workbookPath,
            WorksheetName = "Parts"
        });

        var preflight = Assert.Single(progress.Items, item => item.Phase == WorkbookImportPhase.Preflight).Preflight;
        Assert.NotNull(preflight);
        Assert.Equal(new FileInfo(workbookPath).Length, preflight.CompressedBytes);
        Assert.True(preflight.UncompressedBytes > preflight.CompressedBytes);
        Assert.True(preflight.PackageEntryCount > 0);
    }

    [Fact]
    public async Task Import_rejects_a_Workbook_beyond_the_compressed_safety_ceiling_before_parsing()
    {
        var workbookPath = CreateWorkbook();
        var limits = WorkbookSafetyLimits.DesktopDefault with
        {
            MaximumCompressedBytes = new FileInfo(workbookPath).Length - 1
        };
        var service = new XlsxImportService(DemoMaterialCatalog.All, safetyLimits: limits);

        var response = await service.ImportAsync(new ImportRequest { FilePath = workbookPath });

        var error = Assert.Single(response.Errors);
        Assert.Equal("workbook-safety-ceiling-exceeded", error.Code);
        Assert.Contains("compressed size", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Import_honors_cancellation_at_the_validation_checkpoint()
    {
        var workbookPath = CreateWorkbook();
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress(item =>
        {
            if (item.Phase == WorkbookImportPhase.Validating)
            {
                cancellation.Cancel();
            }
        });
        var service = new XlsxImportService(DemoMaterialCatalog.All, progress: progress);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ImportAsync(
                new ImportRequest { FilePath = workbookPath, WorksheetName = "Parts" },
                cancellation.Token));

        Assert.Equal(
            [
                WorkbookImportPhase.Preflight,
                WorkbookImportPhase.OpeningWorkbook,
                WorkbookImportPhase.ReadingWorksheet,
                WorkbookImportPhase.Validating
            ],
            progress.Items.Select(item => item.Phase));
    }

    [Theory]
    [InlineData("expanded-size")]
    [InlineData("largest-entry")]
    [InlineData("entry-count")]
    [InlineData("compression-ratio")]
    public async Task Import_enforces_each_package_characteristic_ceiling(string characteristic)
    {
        var workbookPath = CreateWorkbook();
        var limits = characteristic switch
        {
            "expanded-size" => WorkbookSafetyLimits.DesktopDefault with { MaximumUncompressedBytes = 1 },
            "largest-entry" => WorkbookSafetyLimits.DesktopDefault with { MaximumLargestEntryBytes = 1 },
            "entry-count" => WorkbookSafetyLimits.DesktopDefault with { MaximumPackageEntryCount = 1 },
            "compression-ratio" => WorkbookSafetyLimits.DesktopDefault with { MaximumCompressionRatio = 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(characteristic))
        };
        var service = new XlsxImportService(DemoMaterialCatalog.All, safetyLimits: limits);

        var response = await service.ImportAsync(new ImportRequest { FilePath = workbookPath });

        Assert.Equal("workbook-safety-ceiling-exceeded", Assert.Single(response.Errors).Code);
    }

    [Fact]
    public async Task Import_returns_clear_guidance_when_package_characteristics_cross_warning_thresholds()
    {
        var workbookPath = CreateWorkbook();
        var limits = WorkbookSafetyLimits.DesktopDefault with
        {
            WarningCompressedBytes = 0,
            WarningUncompressedBytes = 0,
            WarningPackageEntryCount = 0,
            WarningLargestEntryBytes = 0,
            WarningCompressionRatio = 0,
            MaximumCompressedBytes = long.MaxValue,
            MaximumUncompressedBytes = long.MaxValue,
            MaximumPackageEntryCount = int.MaxValue,
            MaximumLargestEntryBytes = long.MaxValue,
            MaximumCompressionRatio = double.MaxValue
        };
        var service = new XlsxImportService(DemoMaterialCatalog.All, safetyLimits: limits);

        var response = await service.ImportAsync(new ImportRequest { FilePath = workbookPath });

        Assert.True(response.Success);
        var safetyWarnings = response.Warnings
            .Where(warning => warning.Code == "workbook-safety-warning")
            .ToArray();
        Assert.Equal(5, safetyWarnings.Length);
        Assert.Contains(safetyWarnings, warning =>
            warning.Message.Contains("split", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(safetyWarnings, warning =>
            warning.Message.Contains("memory-intensive", StringComparison.OrdinalIgnoreCase));
    }

    private string CreateWorkbook()
    {
        Directory.CreateDirectory(_workspacePath);
        var path = Path.Combine(_workspacePath, "parts.xlsx");
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Parts");
        worksheet.Cell("A1").Value = "Id";
        worksheet.Cell("B1").Value = "Length";
        worksheet.Cell("C1").Value = "Width";
        worksheet.Cell("D1").Value = "Quantity";
        worksheet.Cell("E1").Value = "Material";
        worksheet.Cell("A2").Value = "P-001";
        worksheet.Cell("B2").Value = 24;
        worksheet.Cell("C2").Value = 12;
        worksheet.Cell("D2").Value = 1;
        worksheet.Cell("E2").Value = "Demo Material";
        workbook.SaveAs(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
    }

    private sealed class RecordingProgress(Action<WorkbookImportProgress>? onReport = null)
        : IProgress<WorkbookImportProgress>
    {
        public List<WorkbookImportProgress> Items { get; } = [];

        public void Report(WorkbookImportProgress value)
        {
            Items.Add(value);
            onReport?.Invoke(value);
        }
    }
}
