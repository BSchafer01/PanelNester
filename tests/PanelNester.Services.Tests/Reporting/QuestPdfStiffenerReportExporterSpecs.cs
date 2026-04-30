using System.Text;
using PanelNester.Domain.Models;
using PanelNester.Services.Reporting;

namespace PanelNester.Services.Tests.Reporting;

public sealed class QuestPdfStiffenerReportExporterSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(Path.GetTempPath(), $"PanelNester.QuestPdfStiffenerReportExporterSpecs.{Guid.NewGuid():N}");

    [Fact]
    public async Task Export_async_writes_a_pdf_file_for_stiffener_report_data()
    {
        Directory.CreateDirectory(_workspacePath);

        var filePath = Path.Combine(_workspacePath, "stiffener-report.pdf");
        var exporter = new QuestPdfStiffenerReportExporter();

        await exporter.ExportAsync(
            new StiffenerTakeoffReportData
            {
                ProjectMetadata = new ProjectMetadata
                {
                    ProjectName = "Workshop Cabinets",
                    ProjectNumber = "PN-500",
                    CustomerName = "Northwind Fixtures",
                    Date = new DateTime(2026, 03, 14, 0, 0, 0, DateTimeKind.Utc),
                    RequiredDate = new DateTime(2026, 03, 28, 0, 0, 0, DateTimeKind.Utc)
                },
                Settings = new StiffenerTakeoffSettings
                {
                    Enabled = true,
                    MinimumLengthInches = 32m,
                    MinimumWidthInches = 32m,
                    WidthDeductionInches = 4m,
                    StockLengthFeet = 20m,
                    Extrusion = "1x2 aluminum tube"
                },
                OverallSummary = new StiffenerTakeoffSectionSummary
                {
                    EligiblePanelCount = 3,
                    TotalStiffenerCount = 6,
                    TotalLinearFeet = 24.5m,
                    StockLengthFeet = 20m,
                    RequiredStockCount = 2
                },
                OverallLengths =
                [
                    new StiffenerTakeoffLengthSummary
                    {
                        Label = "S44",
                        LengthInches = 44m,
                        PieceCount = 4
                    },
                    new StiffenerTakeoffLengthSummary
                    {
                        Label = "S56",
                        LengthInches = 56m,
                        PieceCount = 2
                    }
                ],
                Materials = [],
                HasTakeoff = true
            },
            filePath);

        Assert.True(File.Exists(filePath));
        var bytes = await File.ReadAllBytesAsync(filePath);
        Assert.True(bytes.Length > 0);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 5)));
    }

    [Fact]
    public async Task Export_async_honors_cancellation()
    {
        Directory.CreateDirectory(_workspacePath);

        var filePath = Path.Combine(_workspacePath, "cancelled-stiffener-report.pdf");
        var exporter = new QuestPdfStiffenerReportExporter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            exporter.ExportAsync(new StiffenerTakeoffReportData(), filePath, cts.Token));

        Assert.False(File.Exists(filePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }
}
