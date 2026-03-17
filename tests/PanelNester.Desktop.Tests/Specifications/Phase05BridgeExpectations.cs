namespace PanelNester.Desktop.Tests.Specifications;

internal static class Phase05BridgeExpectations
{
    internal static IReadOnlyList<string> MessageTypes { get; } =
    [
        "run-batch-nesting",
        "update-report-settings",
        "export-pdf-report"
    ];
}
