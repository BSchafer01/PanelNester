using System.IO;
using System.Windows;
using System.Xml.Linq;

namespace PanelNester.Desktop.Tests.ProjectConfiguration;

public sealed class DesktopProjectConfigurationTests
{
    [Fact]
    public void Desktop_project_targets_windows_and_uses_wpf()
    {
        var projectFile = XDocument.Load(GetRepositoryPath("src", "PanelNester.Desktop", "PanelNester.Desktop.csproj"));
        var propertyGroup = projectFile.Root?.Element("PropertyGroup");

        Assert.NotNull(propertyGroup);
        Assert.Equal("net8.0-windows", propertyGroup?.Element("TargetFramework")?.Value);
        Assert.Equal("true", propertyGroup?.Element("UseWPF")?.Value);
    }

    [Fact]
    public void Main_window_exists_as_the_shell_entry_point()
    {
        Assert.True(typeof(Window).IsAssignableFrom(typeof(PanelNester.Desktop.MainWindow)));
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var pathSegments = new List<string> { AppContext.BaseDirectory, "..", "..", "..", "..", ".." };
        pathSegments.AddRange(segments);
        return Path.GetFullPath(Path.Combine(pathSegments.ToArray()));
    }
}
