using System.Globalization;

namespace PanelNester.Domain.Models;

public static class InchMeasurementParser
{
    public static bool TryParse(string? rawValue, out decimal value)
    {
        value = 0;
        var text = rawValue?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            decimal.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole) &&
            TryParseFraction(parts[1], out var fraction))
        {
            value = whole < 0 ? whole - fraction : whole + fraction;
            return true;
        }

        return parts.Length == 1 &&
            (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) ||
             TryParseFraction(text, out value));
    }

    private static bool TryParseFraction(string text, out decimal value)
    {
        value = 0;
        var slash = text.IndexOf('/');
        if (slash <= 0 || slash != text.LastIndexOf('/') ||
            !decimal.TryParse(text[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numerator) ||
            !decimal.TryParse(text[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var denominator) ||
            denominator == 0)
        {
            return false;
        }

        value = numerator / denominator;
        return true;
    }
}
