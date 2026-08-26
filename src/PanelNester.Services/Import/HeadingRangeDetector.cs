using ClosedXML.Excel;
using PanelNester.Domain.Models;

namespace PanelNester.Services.Import;

internal static class HeadingRangeDetector
{
    private const int MaximumPreviewRows = 25;
    private const int MaximumPreviewColumns = 16;
    private const double HighConfidenceThreshold = 0.72;

    public static ImportWorksheetDescriptor Describe(IXLWorksheet worksheet)
    {
        var usedRange = worksheet.RangeUsed()!;
        var firstRow = usedRange.RangeAddress.FirstAddress.RowNumber;
        var lastRow = Math.Min(usedRange.RangeAddress.LastAddress.RowNumber, firstRow + MaximumPreviewRows - 1);
        var firstColumn = usedRange.RangeAddress.FirstAddress.ColumnNumber;
        var lastColumn = Math.Min(
            usedRange.RangeAddress.LastAddress.ColumnNumber,
            firstColumn + MaximumPreviewColumns - 1);

        var previewRows = Enumerable.Range(firstRow, lastRow - firstRow + 1)
            .Select(rowNumber => new WorksheetPreviewRow
            {
                RowNumber = rowNumber,
                Cells = Enumerable.Range(firstColumn, lastColumn - firstColumn + 1)
                    .Select(columnNumber =>
                    {
                        var cell = worksheet.Cell(rowNumber, columnNumber);
                        return new WorksheetPreviewCell
                        {
                            Address = cell.Address.ToStringRelative(),
                            ColumnNumber = columnNumber,
                            Value = cell.GetString().Trim()
                        };
                    })
                    .ToArray()
            })
            .ToArray();

        var candidates = Enumerable.Range(firstRow, lastRow - firstRow + 1)
            .Select(rowNumber => ScoreRow(worksheet, rowNumber, firstColumn, lastColumn))
            .Where(candidate => candidate is not null)
            .Cast<HeadingRangeCandidate>()
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Address, StringComparer.Ordinal)
            .ToArray();
        var highConfidence = candidates.Where(candidate => candidate.IsHighConfidence).ToArray();
        var topHighConfidence = highConfidence.Length == 0
            ? Array.Empty<HeadingRangeCandidate>()
            : highConfidence
                .Where(candidate => Math.Abs(candidate.Confidence - highConfidence[0].Confidence) < 0.0001)
                .ToArray();
        var detectionStatus = topHighConfidence.Length switch
        {
            1 => "unique-high-confidence",
            > 1 => "tied",
            _ when candidates.Length > 0 => "low-confidence",
            _ => "none"
        };

        return new ImportWorksheetDescriptor
        {
            WorksheetName = worksheet.Name,
            OriginalPosition = worksheet.Position,
            HeadingRange = topHighConfidence.Length == 1 ? topHighConfidence[0].Address : string.Empty,
            HeadingRangeDetectionStatus = detectionStatus,
            HeadingRangeCandidates = candidates,
            PreviewRows = previewRows
        };
    }

    private static HeadingRangeCandidate? ScoreRow(
        IXLWorksheet worksheet,
        int rowNumber,
        int firstPreviewColumn,
        int lastPreviewColumn)
    {
        var populatedColumns = Enumerable.Range(firstPreviewColumn, lastPreviewColumn - firstPreviewColumn + 1)
            .Where(columnNumber => !string.IsNullOrWhiteSpace(worksheet.Cell(rowNumber, columnNumber).GetString()))
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
                    worksheet.Cell(rowNumber, columnNumber).GetString().Trim())))
            .Where(item => item.Field is not null)
            .ToArray();
        if (recognized.Length == 0)
        {
            return null;
        }

        var uniqueFields = recognized.Select(item => item.Field!).Distinct(StringComparer.Ordinal).ToArray();
        var requiredCount = uniqueFields.Count(field => ImportFieldNames.Required.Contains(field, StringComparer.Ordinal));
        var optionalCount = uniqueFields.Count(field => ImportFieldNames.Optional.Contains(field, StringComparer.Ordinal));
        var uniqueness = (double)uniqueFields.Length / recognized.Length;
        var plausibleData = PlausibleFollowingData(worksheet, rowNumber, recognized);
        var confidence =
            0.65 * requiredCount / ImportFieldNames.Required.Count +
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
                var value = worksheet.Cell(rowNumber, item.ColumnNumber).GetString().Trim();
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
}
