using System.IO;

namespace PanelNester.Desktop;

internal static class StartupProjectPathResolver
{
    internal const string ProjectFileExtension = ".pnest";

    public static string? Resolve(IReadOnlyList<string>? arguments)
    {
        if (arguments is null)
        {
            return null;
        }

        foreach (var argument in arguments)
        {
            if (TryResolve(argument, out var projectPath))
            {
                return projectPath;
            }
        }

        return null;
    }

    public static bool TryResolve(string? candidate, out string? projectPath)
    {
        projectPath = null;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var trimmed = candidate.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(trimmed))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!string.Equals(Path.GetExtension(fullPath), ProjectFileExtension, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath))
        {
            return false;
        }

        projectPath = fullPath;
        return true;
    }
}
