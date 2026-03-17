namespace PanelNester.Desktop.Tests.Specifications;

internal static class Phase03ProjectBridgeExpectations
{
    internal static IReadOnlyList<string> ProjectMessageTypes { get; } =
    [
        "new-project",
        "open-project",
        "save-project",
        "save-project-as",
        "get-project-metadata",
        "update-project-metadata"
    ];

    internal static IReadOnlyList<string> ProjectErrorCodes { get; } =
    [
        "project-not-found",
        "project-corrupt",
        "project-unsupported-version",
        "project-save-failed"
    ];
}
