using System.IO;
using PanelNester.Desktop;

namespace PanelNester.Desktop.Tests;

public sealed class DesktopStoragePathsSpecs
{
    [Fact]
    public void App_data_root_directory_uses_local_appdata_panelnester()
    {
        var expectedPath = global::System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PanelNester");

        Assert.Equal(expectedPath, DesktopStoragePaths.AppDataRootDirectory);
    }

    [Fact]
    public void Material_library_path_uses_local_appdata_panelnester_materials_json()
    {
        var expectedPath = global::System.IO.Path.Combine(
            DesktopStoragePaths.AppDataRootDirectory,
            "materials.json");

        Assert.Equal(expectedPath, DesktopStoragePaths.MaterialsFilePath);
    }

    [Fact]
    public void WebView2_user_data_path_uses_local_appdata_panelnester_webview2_userdata()
    {
        var expectedPath = global::System.IO.Path.Combine(
            DesktopStoragePaths.AppDataRootDirectory,
            "WebView2",
            "UserData");

        Assert.Equal(expectedPath, DesktopStoragePaths.WebViewUserDataDirectory);
    }
}
