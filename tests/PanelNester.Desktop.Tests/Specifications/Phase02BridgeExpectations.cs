namespace PanelNester.Desktop.Tests.Specifications;

internal static class Phase02BridgeExpectations
{
    internal static IReadOnlyList<string> MaterialMessageTypes { get; } =
    [
        "list-materials",
        "get-material",
        "create-material",
        "update-material",
        "delete-material"
    ];
}
