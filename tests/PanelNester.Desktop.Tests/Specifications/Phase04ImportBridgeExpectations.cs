namespace PanelNester.Desktop.Tests.Specifications;

internal static class Phase04ImportBridgeExpectations
{
    internal static IReadOnlyList<string> ImportMessageTypes { get; } =
    [
        "import-file",
        "update-part-row",
        "delete-part-row",
        "add-part-row"
    ];
}
