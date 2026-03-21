using System.IO;
using System.Xml.Linq;

namespace PanelNester.Desktop.Tests.ProjectConfiguration;

public sealed class InstallerFileAssociationSpecs
{
    [Fact]
    public void Per_user_installer_registers_pnest_icon_and_shell_open_command()
    {
        var authoringPath = GetRepositoryPath("installer", "PanelNester.Installer", "Product.wxs");
        var document = XDocument.Load(authoringPath);
        var authoring = File.ReadAllText(authoringPath);
        XNamespace wix = "http://wixtoolset.org/schemas/v4/wxs";

        var feature = document.Descendants(wix + "Feature")
            .Single(element => (string?)element.Attribute("Id") == "MainFeature");

        Assert.Contains(
            feature.Elements(wix + "ComponentGroupRef"),
            element => (string?)element.Attribute("Id") == "ProjectFileAssociationGroup");

        var registryValues = document.Descendants(wix + "RegistryValue").ToArray();

        Assert.Contains(
            registryValues,
            value =>
                (string?)value.Attribute("Root") == "HKCU" &&
                (string?)value.Attribute("Key") == @"Software\Classes\.pnest" &&
                (string?)value.Attribute("Value") == "PanelNester.Project");

        Assert.Contains(
            registryValues,
            value =>
                (string?)value.Attribute("Root") == "HKCU" &&
                (string?)value.Attribute("Key") == @"Software\Classes\PanelNester.Project\DefaultIcon" &&
                (string?)value.Attribute("Value") == "\"[INSTALLFOLDER]PanelNester.Desktop.exe\",0");

        Assert.Contains(
            registryValues,
            value =>
                (string?)value.Attribute("Root") == "HKCU" &&
                (string?)value.Attribute("Key") == @"Software\Classes\PanelNester.Project\shell\open\command" &&
                (string?)value.Attribute("Value") == "\"[INSTALLFOLDER]PanelNester.Desktop.exe\" \"%1\"");

        Assert.DoesNotContain(@"FileExts\.pnest\UserChoice", authoring, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRepositoryPath(params string[] segments)
    {
        var pathSegments = new List<string> { AppContext.BaseDirectory, "..", "..", "..", "..", ".." };
        pathSegments.AddRange(segments);
        return Path.GetFullPath(Path.Combine(pathSegments.ToArray()));
    }
}
