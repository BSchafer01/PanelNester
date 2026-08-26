using System.Diagnostics;
using System.IO.Compression;
using ClosedXML.Excel;
using PanelNester.Domain.Models;
using PanelNester.Services.Import;

var quick = args.Contains("--quick", StringComparer.Ordinal);
var root = Path.Combine(Path.GetTempPath(), $"OptiFab.WorkbookBenchmarks.{Guid.NewGuid():N}");
Directory.CreateDirectory(root);

try
{
    Console.WriteLine($"Target: {Environment.OSVersion}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; .NET {Environment.Version}");
    Console.WriteLine("profile,compressedMiB,expandedMiB,entries,ratio,elapsedMs,peakWorkingSetMiB,result");

    await RunImportProfileAsync("large", quick ? 10_000 : 75_000, 5, 1);
    await RunImportProfileAsync("wide", quick ? 500 : 5_000, quick ? 50 : 200, 1);
    await RunImportProfileAsync(
        "many-worksheets",
        rows: quick ? 20 : 100,
        columns: 5,
        worksheetCount: quick ? 25 : 200);
    await RunCompressedProfileAsync("highly-compressed", quick ? 16 : 128, quick ? 1 : 1);
    await RunCompressedProfileAsync("pathological", quick ? 32 : 600, 0);
}
finally
{
    Directory.Delete(root, recursive: true);
}

async Task RunImportProfileAsync(
    string name,
    int rows,
    int columns,
    int worksheetCount)
{
    var path = Path.Combine(root, $"{name}.xlsx");
    using (var workbook = new XLWorkbook())
    {
        for (var worksheetIndex = 0; worksheetIndex < worksheetCount; worksheetIndex++)
        {
            var worksheet = workbook.AddWorksheet($"Parts {worksheetIndex + 1}");
            WriteHeadings(worksheet, columns);
            for (var row = 2; row <= rows + 1; row++)
            {
                worksheet.Cell(row, 1).Value = $"P-{worksheetIndex + 1}-{row - 1}";
                worksheet.Cell(row, 2).Value = 24;
                worksheet.Cell(row, 3).Value = 12;
                worksheet.Cell(row, 4).Value = 1;
                worksheet.Cell(row, 5).Value = "Demo Material";
                for (var column = 6; column <= columns; column++)
                {
                    worksheet.Cell(row, column).Value = "repeated benchmark value";
                }
            }
        }
        workbook.SaveAs(path);
    }

    await MeasureAsync(name, path, async cancellationToken =>
    {
        var service = new XlsxImportService(DemoMaterialCatalog.All);
        foreach (var worksheetIndex in Enumerable.Range(1, worksheetCount))
        {
            var response = await service.ImportAsync(
                new ImportRequest
                {
                    FilePath = path,
                    WorksheetName = $"Parts {worksheetIndex}",
                    HeadingRange = "A1:E1"
                },
                cancellationToken);
            if (!response.Success)
            {
                throw new InvalidOperationException(response.Errors.First().Message);
            }
        }
    });
}

async Task RunCompressedProfileAsync(string name, int repeatedMiB, int randomMiB)
{
    var path = Path.Combine(root, $"{name}.xlsx");
    using (var workbook = new XLWorkbook())
    {
        WriteHeadings(workbook.AddWorksheet("Parts"), 5);
        workbook.SaveAs(path);
    }

    using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
    {
        var entry = archive.CreateEntry("xl/benchmark-padding.bin", CompressionLevel.SmallestSize);
        await using var destination = entry.Open();
        var repeated = new byte[1024 * 1024];
        for (var index = 0; index < repeatedMiB; index++)
        {
            await destination.WriteAsync(repeated);
        }
        var random = new byte[1024 * 1024];
        for (var index = 0; index < randomMiB; index++)
        {
            Random.Shared.NextBytes(random);
            await destination.WriteAsync(random);
        }
    }

    await MeasureAsync(name, path, async cancellationToken =>
    {
        var response = await new XlsxImportService(DemoMaterialCatalog.All).ImportAsync(
            new ImportRequest { FilePath = path, WorksheetName = "Parts" },
            cancellationToken);
        var safetyError = response.Errors.FirstOrDefault(error =>
            error.Code == "workbook-safety-ceiling-exceeded");
        if (safetyError is not null)
        {
            throw new WorkbookSafetyException(safetyError.Message);
        }
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Errors.First().Message);
        }
    });
}

async Task MeasureAsync(string name, string path, Func<CancellationToken, Task> operation)
{
    var process = Process.GetCurrentProcess();
    var startingPeak = process.PeakWorkingSet64;
    var stopwatch = Stopwatch.StartNew();
    string result;
    try
    {
        await operation(CancellationToken.None);
        result = "accepted";
    }
    catch (WorkbookSafetyException exception)
    {
        result = $"blocked: {exception.Message.Replace(',', ';')}";
    }
    stopwatch.Stop();
    process.Refresh();

    WorkbookPreflightAssessment? assessment = null;
    try
    {
        assessment = WorkbookPackagePreflight.Inspect(
            path,
            WorkbookSafetyLimits.DesktopDefault with
            {
                MaximumCompressedBytes = long.MaxValue,
                MaximumUncompressedBytes = long.MaxValue,
                MaximumLargestEntryBytes = long.MaxValue,
                MaximumPackageEntryCount = int.MaxValue,
                MaximumCompressionRatio = double.MaxValue
            });
    }
    catch
    {
        // The operation result already records malformed or unreadable packages.
    }

    Console.WriteLine(string.Join(',',
        name,
        ToMiB(assessment?.CompressedBytes ?? new FileInfo(path).Length),
        ToMiB(assessment?.UncompressedBytes ?? 0),
        assessment?.PackageEntryCount ?? 0,
        (assessment?.CompressionRatio ?? 0).ToString("F1"),
        stopwatch.ElapsedMilliseconds,
        ToMiB(Math.Max(0, process.PeakWorkingSet64 - startingPeak)),
        result));
}

static void WriteHeadings(IXLWorksheet worksheet, int columns)
{
    string[] required = ["Id", "Length", "Width", "Quantity", "Material"];
    for (var column = 1; column <= columns; column++)
    {
        worksheet.Cell(1, column).Value = column <= required.Length
            ? required[column - 1]
            : $"Extra {column}";
    }
}

static string ToMiB(long bytes) => (bytes / (1024d * 1024d)).ToString("F1");
