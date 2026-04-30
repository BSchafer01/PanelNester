using System.IO;

namespace PanelNester.Desktop;

public static class DesktopStoragePaths
{
    public static string AppDataRootDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OptiFab");

    public static string MaterialsFilePath =>
        Path.Combine(AppDataRootDirectory, "materials.json");

    public static string MaterialLibrarySettingsFilePath =>
        Path.Combine(AppDataRootDirectory, "material-library-location.json");

    public static string AppSettingsFilePath =>
        Path.Combine(AppDataRootDirectory, "app-settings.json");

    public static string CompanyLogoDirectory =>
        Path.Combine(AppDataRootDirectory, "Assets", "Logo");

    public static string WebViewUserDataDirectory =>
        Path.Combine(AppDataRootDirectory, "WebView2", "UserData");
}
