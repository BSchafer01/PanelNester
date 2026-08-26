using ClosedXML.Excel;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

internal static class HeadingRangeDetector
{
    private const int MaximumPreviewRows = 25;
    private const int MaximumPreviewColumns = 16;
    private const double HighConfidenceThreshold = 0.72;

    public static ImportWorksheetDescriptor Describe(
        IXLWorksheet worksheet,
        ProjectKind projectKind = ProjectKind.Sheet)
    {
        var usedRange = worksheet.RangeUsed()!;
        var firstRow = usedRange.RangeAddress.FirstAddress.RowNumber;
        var lastRow = Math.Min(usedRange.RangeAddress.LastAddress.RowNumber, firstRow + MaximumPreviewRows - 1);
        var firstColumn = usedRange.RangeAddress.FirstAddress.ColumnNumber;
        var usedLastColumn = usedRange.RangeAddress.LastAddress.ColumnNumber;
        var lastColumn = Math.Min(
            usedLastColumn,
            firstColumn + MaximumPreviewColumns - 1);
        var previewColumns = Enumerable.Range(firstColumn, usedLastColumn - firstColumn + 1)
            .Where(columnNumber =>
                columnNumber <= lastColumn || worksheet.Column(columnNumber).IsHidden)
            .ToArray();

        var previewRows = Enumerable.Range(firstRow, lastRow - firstRow + 1)
            .Select(rowNumber => new WorksheetPreviewRow
            {
                RowNumber = rowNumber,
                Cells = previewColumns
                    .Select(columnNumber =>
                    {
                        var cell = worksheet.Cell(rowNumber, columnNumber);
                        var cellValue = WorkbookCellValueReader.Read(cell);
                        return new WorksheetPreviewCell
                        {
                            Address = cell.Address.ToStringRelative(),
                            ColumnNumber = columnNumber,
                            Value = cellValue.Value,
                            IsHidden = worksheet.Row(rowNumber).IsHidden ||
                                       worksheet.Column(columnNumber).IsHidden,
                            IsFormula = cellValue.IsFormula
                        };
                    })
                    .ToArray()
            })
            .ToArray();

        var candidates = Enumerable.Range(firstRow, lastRow - firstRow + 1)
            .Select(rowNumber => ScoreRow(worksheet, rowNumber, firstColumn, lastColumn, projectKind))
            .Where(candidate => candidate is not null)
            .Cast<HeadingRangeCandidate>()
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Address, StringComparer.Ordinal)
            .ToArray();
        var highConfidence = candidates.Where(candidate => candidate.IsHighConfidence).ToArray();
        var topScoreCandidates = candidates.Length == 0
            ? Array.Empty<HeadingRangeCandidate>()
            : candidates
                .Where(candidate => Math.Abs(candidate.Confidence - candidates[0].Confidence) < 0.0001)
                .ToArray();
        var detectionStatus = highConfidence.Length switch
        {
            1 => HeadingRangeDetectionStatuses.UniqueHighConfidence,
            > 1 => HeadingRangeDetectionStatuses.Tied,
            _ when topScoreCandidates.Length > 1 => HeadingRangeDetectionStatuses.Tied,
            _ when candidates.Length > 0 => HeadingRangeDetectionStatuses.LowConfidence,
            _ => HeadingRangeDetectionStatuses.None
        };
        var ambiguousAddresses = (highConfidence.Length > 1
                ? highConfidence
                : topScoreCandidates.Length > 1
                    ? topScoreCandidates
                    : Array.Empty<HeadingRangeCandidate>())
            .Select(candidate => candidate.Address)
            .ToHashSet(StringComparer.Ordinal);
        var presentedCandidates = candidates
            .Select(candidate => candidate with
            {
                IsTied = ambiguousAddresses.Contains(candidate.Address)
            })
            .ToArray();

        return new ImportWorksheetDescriptor
        {
            WorksheetName = worksheet.Name,
            OriginalPosition = worksheet.Position,
            HeadingRange = highConfidence.Length == 1 ? highConfidence[0].Address : string.Empty,
            HeadingRangeDetectionStatus = detectionStatus,
            HeadingRangeCandidates = presentedCandidates,
            PreviewRows = previewRows
        };
    }

    private static HeadingRangeCandidate? ScoreRow(
        IXLWorksheet worksheet,
        int rowNumber,
        int firstPreviewColumn,
        int lastPreviewColumn,
        ProjectKind projectKind)
    {
        var populatedColumns = Enumerable.Range(firstPreviewColumn, lastPreviewColumn - firstPreviewColumn + 1)
            .Where(columnNumber => !string.IsNullOrWhiteSpace(GetCellText(worksheet.Cell(rowNumber, columnNumber))))
            .ToArray();
        if (populatedColumns.Length == 0)
        {
            return null;
        }

        var firstColumn = populatedColumns[0];
        var lastColumn = populatedColumns[^1];
        var recognized = Enumerable.Range(firstColumn, lastColumn - firstColumn + 1)
            .Select(columnNumber => new RecognizedHeading(
                columnNumber,
                ImportMappingResolver.RecognizeHeading(
                    GetCellText(worksheet.Cell(rowNumber, columnNumber)),
                    projectKind)))
            .Where(item => item.Field is not null)
            .ToArray();
        if (recognized.Length == 0)
        {
            return null;
        }

        var uniqueFields = recognized.Select(item => item.Field!).Distinct(StringComparer.Ordinal).ToArray();
        var requiredFields = ImportFieldNames.RequiredFor(projectKind);
        var optionalFields = ImportFieldNames.OptionalFor(projectKind);
        var requiredCount = uniqueFields.Count(field => requiredFields.Contains(field, StringComparer.Ordinal));
        var optionalCount = uniqueFields.Count(field => optionalFields.Contains(field, StringComparer.Ordinal));
        var uniqueness = (double)uniqueFields.Length / recognized.Length;
        var plausibleData = PlausibleFollowingData(worksheet, rowNumber, recognized);
        var confidence =
            0.65 * requiredCount / requiredFields.Count +
            0.10 * Math.Min(optionalCount, 2) / 2 +
            0.10 * uniqueness +
            0.15 * plausibleData;

        return new HeadingRangeCandidate
        {
            Address = $"{XLHelper.GetColumnLetterFromNumber(firstColumn)}{rowNumber}:{XLHelper.GetColumnLetterFromNumber(lastColumn)}{rowNumber}",
            Confidence = Math.Round(confidence, 3),
            IsHighConfidence = confidence >= HighConfidenceThreshold
        };
    }

    private static double PlausibleFollowingData(
        IXLWorksheet worksheet,
        int headingRow,
        IReadOnlyList<RecognizedHeading> recognized)
    {
        var inspected = 0;
        var plausible = 0;
        var lastUsedRow = worksheet.LastRowUsed()?.RowNumber() ?? headingRow;

        for (var rowNumber = headingRow + 1; rowNumber <= Math.Min(lastUsedRow, headingRow + 3); rowNumber++)
        {
            foreach (var item in recognized)
            {
                var value = GetCellText(worksheet.Cell(rowNumber, item.ColumnNumber));
                if (value.Length == 0)
                {
                    continue;
                }

                inspected++;
                var field = item.Field!;
                var expectsNumber = field is ImportFieldNames.Length or ImportFieldNames.Width or ImportFieldNames.Quantity;
                if (!expectsNumber || double.TryParse(value, out _))
                {
                    plausible++;
                }
            }
        }

        return inspected == 0 ? 0 : (double)plausible / inspected;
    }

    private sealed record RecognizedHeading(int ColumnNumber, string? Field);

    private static string GetCellText(IXLCell cell) => WorkbookCellValueReader.ReadText(cell);
}
