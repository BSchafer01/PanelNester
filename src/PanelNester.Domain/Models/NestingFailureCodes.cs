namespace PanelNester.Domain.Models;

public static class NestingFailureCodes
{
    public const string OutsideUsableSheet = "outside-usable-sheet";
    public const string NoLayoutSpace = "no-layout-space";
    public const string InvalidInput = "invalid-input";
    public const string EmptyRun = "empty-run";

    public static IReadOnlyList<string> All { get; } =
        [OutsideUsableSheet, NoLayoutSpace, InvalidInput, EmptyRun];
}
