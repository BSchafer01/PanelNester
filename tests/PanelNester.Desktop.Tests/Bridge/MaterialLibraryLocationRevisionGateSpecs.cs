using System.IO;
using System.Linq;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class MaterialLibraryLocationRevisionGateSpecs
{
    [Fact]
    public void Desktop_bridge_contracts_expose_active_library_location_plus_repoint_and_restore_actions()
    {
        var bridgeContracts = Normalize(ReadRepositoryText("src", "PanelNester.Desktop", "Bridge", "BridgeContracts.cs"));

        Assert.Contains("public const string ChooseMaterialLibraryLocation = \"choose-material-library-location\";", bridgeContracts);
        Assert.Contains("public const string RestoreDefaultMaterialLibraryLocation = \"restore-default-material-library-location\";", bridgeContracts);
        Assert.Contains("public sealed record ChooseMaterialLibraryLocationRequest", bridgeContracts);
        Assert.Contains("public sealed record ChooseMaterialLibraryLocationResponse", bridgeContracts);
        Assert.Contains("public sealed record RestoreDefaultMaterialLibraryLocationRequest", bridgeContracts);
        Assert.Contains("public sealed record RestoreDefaultMaterialLibraryLocationResponse", bridgeContracts);
    }

    [Fact]
     public void Desktop_host_reads_a_persisted_library_location_from_the_shared_repository_store()
     {
         var storagePaths = Normalize(ReadRepositoryText("src", "PanelNester.Desktop", "DesktopStoragePaths.cs"));
         var mainWindow = Normalize(ReadRepositoryText("src", "PanelNester.Desktop", "MainWindow.xaml.cs"));
 
        Assert.Contains("public static string MaterialsFilePath =>", storagePaths);
        Assert.Contains("public static string MaterialLibrarySettingsFilePath =>", storagePaths);

         Assert.DoesNotContain("new JsonMaterialRepository(DesktopStoragePaths.MaterialsFilePath)", mainWindow);
         Assert.Contains("new JsonMaterialRepository(", mainWindow);
         Assert.Contains("DefaultFilePath = DesktopStoragePaths.MaterialsFilePath", mainWindow);
         Assert.Contains("LocationStoreFilePath = DesktopStoragePaths.MaterialLibrarySettingsFilePath", mainWindow);
         Assert.Contains("_materialLibraryLocationService = materialRepository;", mainWindow);
         Assert.Contains("materialLibraryLocationService: _materialLibraryLocationService", mainWindow);
     }

    [Fact]
    public void Bridge_registration_routes_change_and_restore_requests_through_the_location_service()
    {
         var bridgeRegistration = Normalize(ReadRepositoryText("src", "PanelNester.Desktop", "Bridge", "DesktopBridgeRegistration.cs"));
 
         Assert.Contains("BridgeMessageTypes.ChooseMaterialLibraryLocation", bridgeRegistration);
         Assert.Contains("BridgeMessageTypes.RestoreDefaultMaterialLibraryLocation", bridgeRegistration);
         Assert.Contains("\"Choose material library location\"", bridgeRegistration);
         Assert.Contains(".SaveAsync(", bridgeRegistration);
         Assert.Contains("var location = await materialLibraryLocationService", bridgeRegistration);
         Assert.Contains(".RepointAsync(dialogResult.FilePath, cancellationToken)", bridgeRegistration);
         Assert.Contains(".RestoreDefaultAsync(cancellationToken)", bridgeRegistration);
         Assert.Contains(".GetLocationAsync(cancellationToken)", bridgeRegistration);
     }

    private static string Normalize(string value) => value.Replace("\r\n", "\n");

    private static string ReadRepositoryText(params string[] segments)
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var path = Path.Combine(new[] { repositoryRoot }.Concat(segments).ToArray());
        return File.ReadAllText(path);
    }
}
