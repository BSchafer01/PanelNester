using System.IO;

namespace PanelNester.Desktop.Bridge;

public sealed record WebUiContentLocation(string ContentRoot, string DisplayName, bool IsWebUiBuild);

public static class WebUiContentResolver
{
    public static WebUiContentLocation Resolve(string appBaseDirectory)
    {
        var candidateRoots = EnumerateCandidateRoots(appBaseDirectory).ToArray();
        var bundledContentRoot = Path.Combine(Path.GetFullPath(appBaseDirectory), "WebApp");

        if (IsBundledWebUiBuild(bundledContentRoot))
        {
            return new WebUiContentLocation(
                bundledContentRoot,
                @"Bundled Web UI build (WebApp)",
                true);
        }

        foreach (var directory in candidateRoots)
        {
            var futureBuild = Path.Combine(directory, "src", "PanelNester.WebUI", "dist");
            if (File.Exists(Path.Combine(futureBuild, "index.html")))
            {
                return new WebUiContentLocation(
                    futureBuild,
                    @"Web UI build (src\PanelNester.WebUI\dist)",
                    true);
            }

        }

        if (File.Exists(Path.Combine(bundledContentRoot, "index.html")))
        {
            return new WebUiContentLocation(bundledContentRoot, "Bundled placeholder page", false);
        }

        foreach (var directory in candidateRoots)
        {
            var sourcePlaceholder = Path.Combine(directory, "src", "PanelNester.Desktop", "WebApp");
            if (File.Exists(Path.Combine(sourcePlaceholder, "index.html")))
            {
                return new WebUiContentLocation(sourcePlaceholder, "Source placeholder page", false);
            }
        }

        throw new DirectoryNotFoundException("No web content was found for the desktop host.");
    }

    private static bool IsBundledWebUiBuild(string contentRoot) =>
        File.Exists(Path.Combine(contentRoot, "index.html")) &&
        Directory.Exists(Path.Combine(contentRoot, "assets"));

    private static IEnumerable<string> EnumerateCandidateRoots(string startDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));

        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }
}
