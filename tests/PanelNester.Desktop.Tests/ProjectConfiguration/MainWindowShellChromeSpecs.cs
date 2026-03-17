using System.IO;
using PanelNester.Desktop;

namespace PanelNester.Desktop.Tests.ProjectConfiguration;

public sealed class MainWindowShellChromeSpecs
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_window_title_falls_back_to_default_when_web_title_is_missing(string? documentTitle)
    {
        Assert.Equal(MainWindow.DefaultWindowTitle, MainWindow.ResolveWindowTitle(documentTitle));
    }

    [Fact]
    public void Resolve_window_title_uses_the_web_document_title_for_dirty_project_identity()
    {
        Assert.Equal(
            "Test Job * — PanelNester",
            MainWindow.ResolveWindowTitle("  Test Job * — PanelNester  "));
    }

    [Fact]
    public void Main_window_xaml_no_longer_contains_legacy_native_header_or_footer_chrome()
    {
        var xaml = File.ReadAllText(GetRepositoryPath("src", "PanelNester.Desktop", "MainWindow.xaml"));

        Assert.DoesNotContain("Desktop host foundation for WebView2", xaml);
        Assert.DoesNotContain("Initializing desktop host...", xaml);
        Assert.DoesNotContain("ContentSourceTextBlock", xaml);
        Assert.DoesNotContain("StatusTextBlock", xaml);
        Assert.Contains("WindowTitleTextBlock", xaml);
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var pathSegments = new List<string> { AppContext.BaseDirectory, "..", "..", "..", "..", ".." };
        pathSegments.AddRange(segments);
        return Path.GetFullPath(Path.Combine(pathSegments.ToArray()));
    }
}
