using System.IO;

namespace PanelNester.Desktop;

public static class DesktopStoragePaths
{
    public static string AppDataRootDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PanelNester");

    public static string MaterialsFilePath =>
        Path.Combine(AppDataRootDirectory, "materials.json");

    public static string WebViewUserDataDirectory =>
        Path.Combine(AppDataRootDirectory, "WebView2", "UserData");
}
