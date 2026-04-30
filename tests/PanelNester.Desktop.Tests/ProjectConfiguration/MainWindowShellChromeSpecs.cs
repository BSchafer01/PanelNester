using System.IO;
using System.Windows;
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
            "Test Job * — OptiFab",
            MainWindow.ResolveWindowTitle("  Test Job * — OptiFab  "));
    }

    [Theory]
    [InlineData("Test Job * — OptiFab")]
    [InlineData("  Test Job * — OptiFab  ")]
    public void Has_unsaved_project_changes_detects_the_dirty_window_title_marker(string documentTitle)
    {
        Assert.True(MainWindow.HasUnsavedProjectChanges(documentTitle));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Test Job — OptiFab")]
    [InlineData("Literal * In Name — OptiFab")]
    public void Has_unsaved_project_changes_ignores_clean_window_titles(string? documentTitle)
    {
        Assert.False(MainWindow.HasUnsavedProjectChanges(documentTitle));
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

    [Fact]
    public void Main_window_code_handles_native_maximize_bounds_inside_the_monitor_work_area()
    {
        var codeBehind = File.ReadAllText(GetRepositoryPath("src", "PanelNester.Desktop", "MainWindow.xaml.cs"));

        Assert.Contains("WindowMessageGetMinMaxInfo", codeBehind);
        Assert.Contains("UpdateMaximizedBounds", codeBehind);
        Assert.Contains("MonitorFromWindow(hwnd, MonitorDefaultToNearest)", codeBehind);
        Assert.Contains("minMaxInfo.MaxSize", codeBehind);
    }

    [Fact]
    public void Constrain_window_bounds_keeps_the_window_inside_the_current_work_area()
    {
        var constrained = MainWindow.ConstrainWindowBounds(
            new Rect(-120, -80, 1440, 900),
            new Rect(0, 0, 1280, 720),
            new Size(MainWindow.DefaultMinWindowWidth, MainWindow.DefaultMinWindowHeight));

        Assert.Equal(new Rect(0, 0, 1280, 720), constrained);
    }

    [Fact]
    public void Constrain_window_bounds_preserves_a_visible_window_when_the_monitor_is_smaller_than_the_default_minimum()
    {
        var constrained = MainWindow.ConstrainWindowBounds(
            new Rect(100, 80, 900, 640),
            new Rect(0, 0, 640, 480),
            new Size(MainWindow.DefaultMinWindowWidth, MainWindow.DefaultMinWindowHeight));

        Assert.Equal(new Rect(0, 0, 640, 480), constrained);
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var pathSegments = new List<string> { AppContext.BaseDirectory, "..", "..", "..", "..", ".." };
        pathSegments.AddRange(segments);
        return Path.GetFullPath(Path.Combine(pathSegments.ToArray()));
    }
}
