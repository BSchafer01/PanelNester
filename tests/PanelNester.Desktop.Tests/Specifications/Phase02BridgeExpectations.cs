namespace PanelNester.Desktop.Tests.Specifications;

internal static class Phase02BridgeExpectations
{
    internal static IReadOnlyList<string> MaterialMessageTypes { get; } =
    [
        "list-materials",
        "choose-material-library-location",
        "restore-default-material-library-location",
        "get-material",
        "create-material",
        "update-material",
        "delete-material"
    ];
}
