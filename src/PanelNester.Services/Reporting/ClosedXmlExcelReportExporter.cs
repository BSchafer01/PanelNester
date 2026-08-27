using ClosedXML.Excel;
using PanelNester.Domain.Contracts;
using PanelNester.Domain.Models;
using System.Globalization;
using System.IO;

namespace PanelNester.Services.Reporting;

public sealed class ClosedXmlExcelReportExporter : IExcelReportExporter, IStockLengthExcelReportExporter
{
    private const string SummaryWorksheetName = "Summary";
    private const string ProjectSummaryWorksheetName = "Project Summary";
    private const int SummaryColumnCount = 6;
    private const int PatternColumnCount = 8;

    public Task ExportAsync(
        StockLengthReportData report,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        using var workbook = new XLWorkbook();
        WriteStockLengthSummary(workbook.Worksheets.Add("Summary"), report, cancellationToken);
        WriteStockLengthCutPlans(workbook.Worksheets.Add("Cut Plans"), report, cancellationToken);
        WriteStockLengthUnplaced(workbook.Worksheets.Add("Unplaced"), report, cancellationToken);
        workbook.SaveAs(filePath);
        return Task.CompletedTask;
    }

    public Task ExportAsync(
        ReportData report,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        cancellationToken.ThrowIfCancellationRequested();

        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        using var workbook = new XLWorkbook();
        var materialsByName = report.Materials
            .GroupBy(material => material.MaterialName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        if (report.OptimizationGroups.Count > 0)
        {
            var projectSummary = workbook.Worksheets.Add(ProjectSummaryWorksheetName);
            WriteSummaryWorksheet(
                projectSummary,
                "Project Material Summary",
                report.Materials.Select(ToSummaryRow).ToArray());
            FinalizeWorksheet(projectSummary);

            foreach (var optimizationGroup in report.OptimizationGroups.OrderBy(group => group.Order))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var worksheet = workbook.Worksheets.Add(BuildUniqueWorksheetName(workbook, optimizationGroup.Name));
                var nextRow = WriteSummaryWorksheet(
                    worksheet,
                    DisplayGroupName(optimizationGroup.Name),
                    optimizationGroup.Materials.Select(ToSummaryRow).ToArray());

                nextRow += 2;
                var partGroups = optimizationGroup.PartGroups.Count > 0
                    ? optimizationGroup.PartGroups
                    :
                    [
                        new ReportMaterialSummaryGroup
                        {
                            Materials = optimizationGroup.Materials
                                .Select(material => new ReportMaterialSummaryRow
                                {
                                    MaterialName = material.MaterialName,
                                    MaterialId = material.MaterialId,
                                    SheetLength = material.SheetLength,
                                    SheetWidth = material.SheetWidth,
                                    Summary = material.Summary
                                })
                                .ToArray()
                        }
                    ];

                foreach (var partGroup in partGroups)
                {
                    foreach (var material in partGroup.Materials)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var materialSection = optimizationGroup.Materials.FirstOrDefault(section =>
                            string.Equals(section.MaterialName, material.MaterialName, StringComparison.Ordinal));
                        if (materialSection is null)
                        {
                            continue;
                        }

                        var patterns = BuildPatternRows(partGroup.GroupName, material.MaterialName, materialSection);
                        if (patterns.Count == 0)
                        {
                            continue;
                        }

                        var sectionTitle = partGroups.Count > 1 || !string.IsNullOrWhiteSpace(partGroup.GroupName)
                            ? $"{DisplayGroupName(partGroup.GroupName)} — {DisplayMaterialName(material.MaterialName)}"
                            : DisplayMaterialName(material.MaterialName);
                        nextRow = WritePatternWorksheetSection(worksheet, nextRow, sectionTitle, patterns);
                        nextRow += 2;
                    }
                }

                FinalizeWorksheet(worksheet);
            }
        }
        else if (report.MaterialSummaryGroups.Count > 0)
        {
            foreach (var group in report.MaterialSummaryGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var worksheet = workbook.Worksheets.Add(BuildWorksheetName(group.GroupName));
                var nextRow = WriteSummaryWorksheet(
                    worksheet,
                    DisplayGroupName(group.GroupName),
                    group.Materials.Select(ToSummaryRow).ToArray());

                nextRow += 2;

                foreach (var material in group.Materials)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!materialsByName.TryGetValue(material.MaterialName, out var materialSection))
                    {
                        continue;
                    }

                    var patterns = BuildPatternRows(group.GroupName, material.MaterialName, materialSection);
                    if (patterns.Count == 0)
                    {
                        continue;
                    }

                    nextRow = WritePatternWorksheetSection(
                        worksheet,
                        nextRow,
                        DisplayMaterialName(material.MaterialName),
                        patterns);
                    nextRow += 2;
                }

                FinalizeWorksheet(worksheet);
            }
        }
        else
        {
            var worksheet = workbook.Worksheets.Add(SummaryWorksheetName);
            WriteSummaryWorksheet(
                worksheet,
                "Material Summary",
                report.Materials.Select(ToSummaryRow).ToArray());
            FinalizeWorksheet(worksheet);
        }

        workbook.SaveAs(filePath);
        return Task.CompletedTask;
    }

    private static int WriteSummaryWorksheet(
        IXLWorksheet worksheet,
        string title,
        IReadOnlyList<SummaryRow> rows)
    {
        worksheet.Cell(1, 1).Value = title;
        worksheet.Range(1, 1, 1, SummaryColumnCount).Merge();
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 14;

        worksheet.Cell(3, 1).Value = "Material";
        worksheet.Cell(3, 2).Value = "Sheets";
        worksheet.Cell(3, 3).Value = "Placed";
        worksheet.Cell(3, 4).Value = "Unplaced";
        worksheet.Cell(3, 5).Value = "Utilization";
        worksheet.Cell(3, 6).Value = "Sheet Size";

        var headerRange = worksheet.Range(3, 1, 3, SummaryColumnCount);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

        var currentRow = 4;
        foreach (var row in rows)
        {
            worksheet.Cell(currentRow, 1).Value = row.MaterialName;
            worksheet.Cell(currentRow, 2).Value = row.TotalSheets;
            worksheet.Cell(currentRow, 3).Value = row.TotalPlaced;
            worksheet.Cell(currentRow, 4).Value = row.TotalUnplaced;
            worksheet.Cell(currentRow, 5).Value = row.OverallUtilization / 100m;
            worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "0.0%";
            worksheet.Cell(currentRow, 6).Value = row.SheetSize;
            currentRow++;
        }

        if (rows.Count > 0)
        {
            var table = worksheet.Range(3, 1, currentRow - 1, SummaryColumnCount).CreateTable();
            table.Theme = XLTableTheme.TableStyleMedium2;
        }

        worksheet.SheetView.FreezeRows(3);
        return currentRow;
    }

    private static void WriteStockLengthSummary(
        IXLWorksheet worksheet,
        StockLengthReportData report,
        CancellationToken cancellationToken)
    {
        string[] headings =
        [
            "Optimization Group", "Stock Group", "Profile Number", "Finish", "Stock Item",
            "Placed Piece Instances", "Stock Length", "Piece Length", "Saw Loss", "Remainder", "Utilization", "Status"
        ];
        WriteHeadings(worksheet, headings);

        var row = 2;
        foreach (var group in report.OptimizationGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var stockGroup in group.StockGroups)
            {
                foreach (var stockItem in stockGroup.StockItems)
                {
                    worksheet.Cell(row, 1).Value = group.Name;
                    worksheet.Cell(row, 2).Value = StockGroupLabel(stockGroup.ProfileNumber, stockGroup.Finish);
                    worksheet.Cell(row, 3).Value = stockGroup.ProfileNumber;
                    worksheet.Cell(row, 4).Value = stockGroup.Finish ?? string.Empty;
                    worksheet.Cell(row, 5).Value = stockItem.StockItemNumber;
                    worksheet.Cell(row, 6).Value = stockItem.CutSequence.Count;
                    worksheet.Cell(row, 7).Value = stockItem.StockLength;
                    worksheet.Cell(row, 8).Value = stockItem.PieceLength;
                    worksheet.Cell(row, 9).Value = stockItem.SawLoss;
                    worksheet.Cell(row, 10).Value = stockItem.Remainder;
                    worksheet.Cell(row, 11).Value = stockItem.UtilizationPercent / 100m;
                    worksheet.Cell(row, 11).Style.NumberFormat.Format = "0.0%";
                    worksheet.Cell(row, 12).Value = DisplayState(stockGroup.State);
                    row++;
                }
            }
        }

        CreateFilterableTable(worksheet, headings.Length, row);
        WriteScopeStatusTable(worksheet, report, row + 2);
    }

    private static void WriteStockLengthCutPlans(
        IXLWorksheet worksheet,
        StockLengthReportData report,
        CancellationToken cancellationToken)
    {
        string[] headings =
        [
            "Optimization Group", "Stock Group", "Profile Number", "Finish", "Stock Item", "Cut Sequence",
            "Piece Instance", "Quantity Instance", "Required Piece", "Part Number", "Part Name", "Length",
            "Start Position", "End Position", "Source References", "Status"
        ];
        WriteHeadings(worksheet, headings);

        var row = 2;
        foreach (var group in report.OptimizationGroups)
        {
            foreach (var stockGroup in group.StockGroups)
            {
                foreach (var stockItem in stockGroup.StockItems)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var startPosition = 0m;
                    var kerf = stockItem.CutSequence.Count > 1
                        ? stockItem.SawLoss / (stockItem.CutSequence.Count - 1)
                        : 0m;
                    foreach (var piece in stockItem.CutSequence.OrderBy(piece => piece.Sequence))
                    {
                        var endPosition = startPosition + piece.Length;
                        worksheet.Cell(row, 1).Value = group.Name;
                        worksheet.Cell(row, 2).Value = StockGroupLabel(stockGroup.ProfileNumber, stockGroup.Finish);
                        worksheet.Cell(row, 3).Value = stockGroup.ProfileNumber;
                        worksheet.Cell(row, 4).Value = stockGroup.Finish ?? string.Empty;
                        worksheet.Cell(row, 5).Value = stockItem.StockItemNumber;
                        worksheet.Cell(row, 6).Value = piece.Sequence;
                        worksheet.Cell(row, 7).Value = piece.PieceInstanceId;
                        worksheet.Cell(row, 8).Value = piece.QuantityInstance;
                        worksheet.Cell(row, 9).Value = piece.RequiredPieceId;
                        worksheet.Cell(row, 10).Value = piece.PartNumber ?? string.Empty;
                        worksheet.Cell(row, 11).Value = piece.PartName ?? string.Empty;
                        worksheet.Cell(row, 12).Value = piece.Length;
                        worksheet.Cell(row, 13).Value = startPosition;
                        worksheet.Cell(row, 14).Value = endPosition;
                        worksheet.Cell(row, 15).Value = FormatSourceReferences(piece.SourceReferences);
                        worksheet.Cell(row, 16).Value = DisplayState(stockGroup.State);
                        startPosition = endPosition + kerf;
                        row++;
                    }
                }
            }
        }

        CreateFilterableTable(worksheet, headings.Length, row);
    }

    private static void WriteStockLengthUnplaced(
        IXLWorksheet worksheet,
        StockLengthReportData report,
        CancellationToken cancellationToken)
    {
        string[] headings =
        [
            "Optimization Group", "Stock Group", "Profile Number", "Finish", "Piece Instance",
            "Quantity Instance", "Required Piece", "Part Number", "Part Name", "Length", "Source References",
            "Reason Code", "Reason", "Status"
        ];
        WriteHeadings(worksheet, headings);

        var row = 2;
        foreach (var item in report.UnplacedPieceInstances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            worksheet.Cell(row, 1).Value = item.OptimizationGroupName;
            worksheet.Cell(row, 2).Value = StockGroupLabel(item.ProfileNumber, item.Finish);
            worksheet.Cell(row, 3).Value = item.ProfileNumber;
            worksheet.Cell(row, 4).Value = item.Finish ?? string.Empty;
            worksheet.Cell(row, 5).Value = item.PieceInstance.PieceInstanceId;
            worksheet.Cell(row, 6).Value = item.PieceInstance.QuantityInstance;
            worksheet.Cell(row, 7).Value = item.PieceInstance.RequiredPieceId;
            worksheet.Cell(row, 8).Value = item.PieceInstance.PartNumber ?? string.Empty;
            worksheet.Cell(row, 9).Value = item.PieceInstance.PartName ?? string.Empty;
            worksheet.Cell(row, 10).Value = item.PieceInstance.Length;
            worksheet.Cell(row, 11).Value = FormatSourceReferences(item.PieceInstance.SourceReferences);
            worksheet.Cell(row, 12).Value = item.ReasonCode;
            worksheet.Cell(row, 13).Value = item.ReasonDescription;
            worksheet.Cell(row, 14).Value = DisplayState(item.State);
            row++;
        }

        CreateFilterableTable(worksheet, headings.Length, row);
    }

    private static void WriteScopeStatusTable(
        IXLWorksheet worksheet,
        StockLengthReportData report,
        int titleRow)
    {
        worksheet.Cell(titleRow, 1).Value = "Scope Status";
        worksheet.Cell(titleRow, 1).Style.Font.Bold = true;
        string[] headings = ["Optimization Group", "Stock Group", "Profile Number", "Finish", "Status", "Details"];
        var headingRow = titleRow + 1;
        for (var column = 1; column <= headings.Length; column++)
        {
            worksheet.Cell(headingRow, column).Value = headings[column - 1];
        }
        worksheet.Range(headingRow, 1, headingRow, headings.Length).Style.Font.Bold = true;

        var row = headingRow + 1;
        foreach (var group in report.OptimizationGroups)
        {
            if (group.StockGroups.Count == 0)
            {
                WriteScopeStatusRow(worksheet, row++, group, stockGroup: null);
                continue;
            }

            foreach (var stockGroup in group.StockGroups)
            {
                WriteScopeStatusRow(worksheet, row++, group, stockGroup);
            }
        }

        if (row > headingRow + 1)
        {
            worksheet.Range(headingRow, 1, row - 1, headings.Length).CreateTable();
        }
    }

    private static void WriteScopeStatusRow(
        IXLWorksheet worksheet,
        int row,
        StockLengthReportOptimizationGroup group,
        StockLengthReportStockGroup? stockGroup)
    {
        worksheet.Cell(row, 1).Value = group.Name;
        if (stockGroup is not null)
        {
            worksheet.Cell(row, 2).Value = StockGroupLabel(stockGroup.ProfileNumber, stockGroup.Finish);
            worksheet.Cell(row, 3).Value = stockGroup.ProfileNumber;
            worksheet.Cell(row, 4).Value = stockGroup.Finish ?? string.Empty;
        }
        worksheet.Cell(row, 5).Value = DisplayState(stockGroup?.State ?? group.State);
        worksheet.Cell(row, 6).Value = group.FailureMessage ?? string.Empty;
    }

    private static void WriteHeadings(IXLWorksheet worksheet, IReadOnlyList<string> headings)
    {
        for (var column = 1; column <= headings.Count; column++)
        {
            worksheet.Cell(1, column).Value = headings[column - 1];
        }

        var range = worksheet.Range(1, 1, 1, headings.Count);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.LightGray;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        worksheet.SheetView.FreezeRows(1);
    }

    private static void CreateFilterableTable(IXLWorksheet worksheet, int columnCount, int nextRow)
    {
        if (nextRow > 2)
        {
            worksheet.Range(1, 1, nextRow - 1, columnCount).CreateTable();
        }
        else
        {
            worksheet.Range(1, 1, 1, columnCount).SetAutoFilter();
        }
        worksheet.Columns().AdjustToContents();
    }

    private static string StockGroupLabel(string profileNumber, string? finish) =>
        $"{profileNumber} — {(string.IsNullOrWhiteSpace(finish) ? "No finish specified" : finish)}";

    private static string DisplayState(StockLengthReportState state) => state switch
    {
        StockLengthReportState.NeedsGeneration => "Needs Generation",
        StockLengthReportState.ApplicationError => "Application Error",
        _ => state.ToString()
    };

    private static string FormatSourceReferences(IEnumerable<SourceReference> sourceReferences) =>
        string.Join("; ", sourceReferences.Select(reference => $"{reference.WorksheetName}!{reference.PhysicalRow}"));

    private static int WritePatternWorksheetSection(
        IXLWorksheet worksheet,
        int startRow,
        string materialName,
        IReadOnlyList<PatternRow> rows)
    {
        worksheet.Cell(startRow, 1).Value = materialName;
        worksheet.Range(startRow, 1, startRow, PatternColumnCount).Merge();
        worksheet.Cell(startRow, 1).Style.Font.Bold = true;
        worksheet.Cell(startRow, 1).Style.Font.FontSize = 14;

        var headerRow = startRow + 1;
        worksheet.Cell(headerRow, 1).Value = "Sheet";
        worksheet.Cell(headerRow, 2).Value = "Quantity";
        worksheet.Cell(headerRow, 3).Value = "Panels";
        worksheet.Cell(headerRow, 4).Value = "Utilization";
        worksheet.Cell(headerRow, 5).Value = "Required Cuts";
        worksheet.Cell(headerRow, 6).Value = "Panel Count";
        worksheet.Cell(headerRow, 7).Value = "Total Cuts";
        worksheet.Cell(headerRow, 8).Value = "Cut Panels";

        var currentRow = headerRow + 1;
        foreach (var row in rows)
        {
            worksheet.Cell(currentRow, 1).Value = row.SheetLabel;
            worksheet.Cell(currentRow, 2).Value = row.Quantity;
            worksheet.Cell(currentRow, 3).Value = row.Panels;
            worksheet.Cell(currentRow, 4).Value = row.Utilization / 100m;
            worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "0.##%";
            worksheet.Cell(currentRow, 5).Value = row.RequiredCuts;
            worksheet.Cell(currentRow, 6).Value = row.PanelCount;
            worksheet.Cell(currentRow, 7).Value = row.TotalCuts;
            worksheet.Cell(currentRow, 8).Value = row.CutPanels;
            currentRow++;
        }

        var table = worksheet.Range(headerRow, 1, currentRow - 1, PatternColumnCount).CreateTable();
        table.Theme = XLTableTheme.TableStyleMedium2;

        return currentRow;
    }

    private static void FinalizeWorksheet(IXLWorksheet worksheet)
    {
        worksheet.Columns().AdjustToContents();
    }

    private static IReadOnlyList<PatternRow> BuildPatternRows(
        string groupName,
        string materialName,
        ReportMaterialSection materialSection)
    {
        var groupKey = NormalizeGroupName(groupName);
        var patternRows = new List<PatternRow>();
        var patternIndexBySignature = new Dictionary<string, int>(StringComparer.Ordinal);
        var nextPatternNumber = 1;
        PatternAccumulator? basePattern = null;

        foreach (var sheet in materialSection.Sheets.OrderBy(sheet => sheet.SheetNumber))
        {
            var placements = sheet.Placements
                .Where(placement => string.Equals(NormalizeGroupName(placement.Group), groupKey, StringComparison.Ordinal))
                .ToArray();

            if (placements.Length == 0)
            {
                continue;
            }

            var panelIds = placements
                .Select(placement => ToDisplayPanelId(placement.PartId))
                .ToArray();
            var panelCount = panelIds.Length;
            var utilization = CalculatePatternUtilization(sheet, placements);
            var requiredCuts = CalculateRequiredCuts(sheet, placements);
            var signature = BuildPatternSignature(placements, panelIds);

            if (IsBasePattern(materialName, panelIds))
            {
                basePattern ??= new PatternAccumulator(
                    $"{DisplayMaterialName(materialName)}*",
                    string.Join(", ", panelIds),
                    utilization,
                    requiredCuts,
                    panelCount);
                basePattern.Quantity++;
                continue;
            }

            if (!patternIndexBySignature.TryGetValue(signature, out var patternNumber))
            {
                patternNumber = nextPatternNumber++;
                patternIndexBySignature[signature] = patternNumber;
                patternRows.Add(new PatternRow(
                    $"{DisplayMaterialName(materialName)}#{patternNumber}",
                    0,
                    string.Join(", ", panelIds),
                    utilization,
                    requiredCuts,
                    panelCount));
            }

            var patternIndex = patternRows.FindIndex(row =>
                string.Equals(row.SheetLabel, $"{DisplayMaterialName(materialName)}#{patternNumber}", StringComparison.Ordinal));
            var currentPattern = patternRows[patternIndex];
            patternRows[patternIndex] = currentPattern with
            {
                Quantity = currentPattern.Quantity + 1
            };
        }

        if (basePattern is not null)
        {
            patternRows.Insert(0, basePattern.ToRow());
        }

        return patternRows;
    }

    private static SummaryRow ToSummaryRow(ReportMaterialSection material) =>
        new(
            DisplayMaterialName(material.MaterialName),
            material.Summary.TotalSheets,
            material.Summary.TotalPlaced,
            material.Summary.TotalUnplaced,
            material.Summary.OverallUtilization,
            FormatSheetSize(material.SheetLength, material.SheetWidth));

    private static SummaryRow ToSummaryRow(ReportMaterialSummaryRow material) =>
        new(
            DisplayMaterialName(material.MaterialName),
            material.Summary.TotalSheets,
            material.Summary.TotalPlaced,
            material.Summary.TotalUnplaced,
            material.Summary.OverallUtilization,
            FormatSheetSize(material.SheetLength, material.SheetWidth));

    private static string DisplayMaterialName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Unnamed material" : value.Trim();

    private static string DisplayGroupName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Ungrouped" : value.Trim();

    private static string NormalizeGroupName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string BuildWorksheetName(string groupName)
    {
        var normalized = DisplayGroupName(groupName);
        var sanitized = new string(
            normalized
                .Select(character => character is '[' or ']' or '*' or '?' or '/' or '\\' or ':'
                    ? '_'
                    : character)
                .ToArray())
            .Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Ungrouped";
        }

        return sanitized.Length <= 31 ? sanitized : sanitized[..31];
    }

    private static string BuildUniqueWorksheetName(XLWorkbook workbook, string groupName)
    {
        var baseName = BuildWorksheetName(groupName);
        var candidate = baseName;
        var suffix = 2;
        while (workbook.Worksheets.Any(sheet => string.Equals(sheet.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            var suffixText = $" ({suffix++})";
            candidate = $"{baseName[..Math.Min(baseName.Length, 31 - suffixText.Length)]}{suffixText}";
        }

        return candidate;
    }

    private static string FormatSheetSize(decimal sheetLength, decimal sheetWidth) =>
        $"{FormatDimension(sheetLength)} × {FormatDimension(sheetWidth)}";

    private static string FormatDimension(decimal value) =>
        $"{value.ToString("0.###", CultureInfo.InvariantCulture)}\"";

    private static decimal CalculatePatternUtilization(
        ReportSheetDiagram sheet,
        IReadOnlyList<NestPlacement> placements)
    {
        var sheetArea = sheet.SheetLength * sheet.SheetWidth;
        if (sheetArea <= 0m)
        {
            return 0m;
        }

        var usedArea = placements.Sum(placement => placement.Width * placement.Height);
        return decimal.Round((usedArea / sheetArea) * 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static string BuildPatternSignature(
        IReadOnlyList<NestPlacement> placements,
        IReadOnlyList<string> panelIds) =>
        string.Join(
            ";",
            placements.Select((placement, index) =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{panelIds[index]}|{placement.X:0.###}|{placement.Y:0.###}|{placement.Width:0.###}|{placement.Height:0.###}|{placement.Rotated90}")));

    private static int CalculateRequiredCuts(
        ReportSheetDiagram sheet,
        IReadOnlyList<NestPlacement> placements)
    {
        var verticalCuts = placements
            .Select(placement => placement.X + placement.Width)
            .Where(position => position > 0m && position < sheet.SheetLength)
            .Distinct()
            .Count();

        var horizontalCuts = placements
            .Select(placement => placement.Y + placement.Height)
            .Where(position => position > 0m && position < sheet.SheetWidth)
            .Distinct()
            .Count();

        return verticalCuts + horizontalCuts;
    }

    private static bool IsBasePattern(string materialName, IReadOnlyList<string> panelIds) =>
        panelIds.Count == 1 &&
        string.Equals(panelIds[0], DisplayMaterialName(materialName), StringComparison.Ordinal);

    private static string ToDisplayPanelId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(unnamed panel)";
        }

        var trimmed = value.Trim();
        var hashIndex = trimmed.LastIndexOf('#');
        if (hashIndex <= 0 || hashIndex == trimmed.Length - 1)
        {
            return trimmed;
        }

        for (var index = hashIndex + 1; index < trimmed.Length; index++)
        {
            if (!char.IsDigit(trimmed[index]))
            {
                return trimmed;
            }
        }

        return trimmed[..hashIndex];
    }

    private sealed record SummaryRow(
        string MaterialName,
        int TotalSheets,
        int TotalPlaced,
        int TotalUnplaced,
        decimal OverallUtilization,
        string SheetSize);

    private sealed record PatternRow(
        string SheetLabel,
        int Quantity,
        string Panels,
        decimal Utilization,
        int RequiredCuts,
        int PanelCount)
    {
        public int TotalCuts => RequiredCuts * Quantity;

        public int CutPanels => RequiredCuts > 0 ? PanelCount * Quantity : 0;
    }

    private sealed class PatternAccumulator(
        string sheetLabel,
        string panels,
        decimal utilization,
        int requiredCuts,
        int panelCount)
    {
        public int Quantity { get; set; }

        public PatternRow ToRow() => new(sheetLabel, Quantity, panels, utilization, requiredCuts, panelCount);
    }
}
