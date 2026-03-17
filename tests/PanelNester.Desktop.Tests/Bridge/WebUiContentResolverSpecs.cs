using System.IO;
using PanelNester.Desktop.Bridge;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class WebUiContentResolverSpecs : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"panelnester-webui-resolver-{Guid.NewGuid():N}");

    [Fact]
    public void Resolve_prefers_the_bundled_webui_build_when_the_output_contains_a_real_bundle()
    {
        var appBaseDirectory = Path.Combine(_tempRoot, "src", "PanelNester.Desktop", "bin", "Debug", "net8.0-windows");
        var bundledBuildDirectory = Path.Combine(appBaseDirectory, "WebApp");
        Directory.CreateDirectory(Path.Combine(bundledBuildDirectory, "assets"));
        File.WriteAllText(Path.Combine(bundledBuildDirectory, "index.html"), "<html>real-ui</html>");
        File.WriteAllText(Path.Combine(bundledBuildDirectory, "assets", "index.js"), "console.log('real-ui');");

        var distDirectory = Path.Combine(_tempRoot, "src", "PanelNester.WebUI", "dist");
        Directory.CreateDirectory(distDirectory);
        File.WriteAllText(Path.Combine(distDirectory, "index.html"), "<html>real-ui</html>");

        var result = WebUiContentResolver.Resolve(appBaseDirectory);

        Assert.True(result.IsWebUiBuild);
        Assert.Equal(bundledBuildDirectory, result.ContentRoot);
        Assert.Equal(@"Bundled Web UI build (WebApp)", result.DisplayName);
    }

    [Fact]
    public void Resolve_prefers_the_source_webui_build_over_the_bundled_placeholder_when_no_bundled_build_exists()
    {
        var appBaseDirectory = Path.Combine(_tempRoot, "src", "PanelNester.Desktop", "bin", "Debug", "net8.0-windows");
        Directory.CreateDirectory(Path.Combine(appBaseDirectory, "WebApp"));
        File.WriteAllText(Path.Combine(appBaseDirectory, "WebApp", "index.html"), "<html>placeholder</html>");

        var distDirectory = Path.Combine(_tempRoot, "src", "PanelNester.WebUI", "dist");
        Directory.CreateDirectory(distDirectory);
        File.WriteAllText(Path.Combine(distDirectory, "index.html"), "<html>real-ui</html>");

        var result = WebUiContentResolver.Resolve(appBaseDirectory);

        Assert.True(result.IsWebUiBuild);
        Assert.Equal(distDirectory, result.ContentRoot);
        Assert.Equal(@"Web UI build (src\PanelNester.WebUI\dist)", result.DisplayName);
    }

    [Fact]
    public void Resolve_keeps_the_bundled_placeholder_as_a_fallback_when_no_webui_build_exists()
    {
        var appBaseDirectory = Path.Combine(_tempRoot, "src", "PanelNester.Desktop", "bin", "Debug", "net8.0-windows");
        var bundledPlaceholderDirectory = Path.Combine(appBaseDirectory, "WebApp");
        Directory.CreateDirectory(bundledPlaceholderDirectory);
        File.WriteAllText(Path.Combine(bundledPlaceholderDirectory, "index.html"), "<html>placeholder</html>");

        var result = WebUiContentResolver.Resolve(appBaseDirectory);

        Assert.False(result.IsWebUiBuild);
        Assert.Equal(bundledPlaceholderDirectory, result.ContentRoot);
        Assert.Equal("Bundled placeholder page", result.DisplayName);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
