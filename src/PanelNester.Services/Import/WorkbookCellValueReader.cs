using System.Globalization;
using ClosedXML.Excel;

namespace PanelNester.Services.Import;

internal static class WorkbookCellValueReader
{
    public static string ReadText(IXLCell cell) => Read(cell).Value;

    public static WorkbookCellValue Read(IXLCell cell, WorkbookVbaProject? vbaProject = null)
    {
        var formula = cell.FormulaA1;
        if (string.IsNullOrWhiteSpace(formula))
        {
            return new WorkbookCellValue(cell.GetString().Trim(), IsFormula: false);
        }

        if (formula.Contains("_xll.", StringComparison.OrdinalIgnoreCase) ||
            formula.Contains("_xludf.", StringComparison.OrdinalIgnoreCase) ||
            CallsVbaFunction(formula, vbaProject))
        {
            return FormulaError(
                cell,
                "vba-formula-not-supported",
                "uses a VBA or add-in user-defined function. Replace it with a stored literal value before importing.");
        }

        var cachedValue = cell.CachedValue;
        if (cachedValue.IsBlank)
        {
            return FormulaError(
                cell,
                "missing-formula-value",
                "does not have a stored Workbook value. Open and save the Workbook in Excel before importing.");
        }

        if (cachedValue.IsError)
        {
            return FormulaError(
                cell,
                "formula-error",
                $"has the stored formula error {cachedValue.ToString(CultureInfo.InvariantCulture)}. Correct the formula in Excel before importing.");
        }

        if (!cachedValue.IsText && !cachedValue.IsNumber)
        {
            return FormulaError(
                cell,
                "unsupported-formula-result",
                $"has an unsupported stored {cachedValue.Type} result. Replace it with text or a number before importing.");
        }

        return new WorkbookCellValue(
            cachedValue.ToString(CultureInfo.CurrentCulture).Trim(),
            IsFormula: true);
    }

    private static WorkbookCellValue FormulaError(IXLCell cell, string code, string guidance) =>
        new(
            string.Empty,
            IsFormula: true,
            new WorkbookCellReadError(
                code,
                $"Formula cell {cell.Address.ToStringRelative()} {guidance}"));

    private static bool CallsVbaFunction(string formula, WorkbookVbaProject? vbaProject)
    {
        if (vbaProject is null)
        {
            return false;
        }

        for (var index = 0; index < formula.Length; index++)
        {
            if (!IsIdentifierStart(formula[index]))
            {
                continue;
            }

            var start = index;
            while (index + 1 < formula.Length && IsIdentifierPart(formula[index + 1]))
            {
                index++;
            }

            var next = index + 1;
            while (next < formula.Length && char.IsWhiteSpace(formula[next]))
            {
                next++;
            }

            if (next < formula.Length && formula[next] == '(')
            {
                var qualifiedName = formula[start..(index + 1)];
                var identifier = qualifiedName[(qualifiedName.LastIndexOf('.') + 1)..];
                if (vbaProject.DeclaresFunction(identifier))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';

    private static bool IsIdentifierPart(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '.';
}

internal sealed record WorkbookCellValue(
    string Value,
    bool IsFormula,
    WorkbookCellReadError? Error = null);

internal sealed record WorkbookCellReadError(string Code, string Message);
