using System.Windows.Input;
using PanelNester.Desktop;

namespace PanelNester.Desktop.Tests.Bridge;

public sealed class WebViewBridgeShortcutSpecs
{
    [Theory]
    [InlineData(Key.N, ModifierKeys.Control, "new")]
    [InlineData(Key.O, ModifierKeys.Control, "open")]
    [InlineData(Key.S, ModifierKeys.Control, "save")]
    [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Shift, "saveAs")]
    [InlineData(Key.N, ModifierKeys.Control | ModifierKeys.Shift, null)]
    [InlineData(Key.O, ModifierKeys.Control | ModifierKeys.Shift, null)]
    [InlineData(Key.S, ModifierKeys.None, null)]
    [InlineData(Key.S, ModifierKeys.Control | ModifierKeys.Alt, null)]
    [InlineData(Key.P, ModifierKeys.Control, null)]
    public void Project_shortcuts_resolve_to_the_expected_actions(
        Key key,
        ModifierKeys modifiers,
        string? expected)
    {
        var actual = MainWindow.ResolveProjectShortcut(key, modifiers);

        Assert.Equal(expected, actual);
    }
}
