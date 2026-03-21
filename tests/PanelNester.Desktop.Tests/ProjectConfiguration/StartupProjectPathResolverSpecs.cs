using System.IO;
using PanelNester.Desktop;

namespace PanelNester.Desktop.Tests.ProjectConfiguration;

public sealed class StartupProjectPathResolverSpecs : IDisposable
{
    private readonly string _workspacePath = Path.Combine(
        Path.GetTempPath(),
        $"PanelNester.StartupProjectPathResolverSpecs.{Guid.NewGuid():N}");

    [Fact]
    public void Resolve_returns_the_first_existing_absolute_pnest_path()
    {
        Directory.CreateDirectory(_workspacePath);
        var projectPath = Path.Combine(_workspacePath, "sample-project.pnest");
        File.WriteAllText(projectPath, "pnest");

        var resolved = StartupProjectPathResolver.Resolve(
            [
                "not-a-path",
                "\"  \"",
                projectPath
            ]);

        Assert.Equal(projectPath, resolved);
    }

    [Fact]
    public void Try_resolve_rejects_missing_non_project_or_relative_paths()
    {
        Assert.False(StartupProjectPathResolver.TryResolve(@"relative\sample-project.pnest", out _));
        Assert.False(StartupProjectPathResolver.TryResolve(@"C:\missing\sample-project.pnest", out _));
        Assert.False(StartupProjectPathResolver.TryResolve(@"C:\temp\sample-project.txt", out _));
    }

    [Fact]
    public void App_startup_is_code_driven_so_initial_project_paths_can_be_injected()
    {
        var xaml = File.ReadAllText(GetRepositoryPath("src", "PanelNester.Desktop", "App.xaml"));
        var codeBehind = File.ReadAllText(GetRepositoryPath("src", "PanelNester.Desktop", "App.xaml.cs"));

        Assert.DoesNotContain("StartupUri=", xaml, StringComparison.Ordinal);
        Assert.Contains("new MainWindow(StartupProjectPathResolver.Resolve(e.Args))", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_attempts_to_open_an_initial_project_after_webview_initializes()
    {
        var codeBehind = Normalize(File.ReadAllText(GetRepositoryPath("src", "PanelNester.Desktop", "MainWindow.xaml.cs")));

        Assert.Contains("_initialProjectPath = initialProjectPath;", codeBehind);
        Assert.Contains("await TryOpenInitialProjectAsync();", codeBehind);
        Assert.Contains("await _bridge.OpenProjectAsync", codeBehind);
    }

    [Fact]
    public void Initial_project_open_waits_for_the_host_before_reusing_the_existing_open_project_flow()
    {
        var mainWindow = File.ReadAllText(GetRepositoryPath("src", "PanelNester.Desktop", "MainWindow.xaml.cs"));
        var webViewBridge = File.ReadAllText(GetRepositoryPath("src", "PanelNester.Desktop", "Bridge", "WebViewBridge.cs"));
        var app = File.ReadAllText(GetRepositoryPath("src", "PanelNester.WebUI", "src", "App.tsx"));

        Assert.Contains("await TryOpenInitialProjectAsync();", mainWindow, StringComparison.Ordinal);
        Assert.Contains("await WaitForHostReadyAsync(cancellationToken)", webViewBridge, StringComparison.Ordinal);
        Assert.Contains("await desktopHost.openProject", webViewBridge, StringComparison.Ordinal);
        Assert.Contains("panelNesterDesktopHost", app, StringComparison.Ordinal);
        Assert.Contains("const response = await hostBridge.openProject(request);", app, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspacePath))
        {
            Directory.Delete(_workspacePath, true);
        }
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var pathSegments = new List<string> { AppContext.BaseDirectory, "..", "..", "..", "..", ".." };
        pathSegments.AddRange(segments);
        return Path.GetFullPath(Path.Combine(pathSegments.ToArray()));
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n");
}
