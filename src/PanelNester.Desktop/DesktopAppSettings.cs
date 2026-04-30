namespace PanelNester.Desktop;

public sealed record DesktopAppSettings
{
    public string? ActiveMaterialLibraryPath { get; init; }

    public string? CompanyLogoPath { get; init; }

    public string? CompanyName { get; init; }
}
