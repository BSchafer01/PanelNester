using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PanelNester.Desktop;

internal static class NativeTitleBarStyler
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    private const int Enabled = 1;
    private const int ShellCaptionColor = 0x00181818;
    private const int ShellTextColor = 0x00CCCCCC;
    private const int ShellBorderColor = 0x00302D2D;

    internal static void TryApply(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var enabled = Enabled;

        // Windows 10 1809 used attribute 19 before the API stabilized on 20.
        if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
        }

        TrySetColor(handle, DwmwaCaptionColor, ShellCaptionColor);
        TrySetColor(handle, DwmwaTextColor, ShellTextColor);
        TrySetColor(handle, DwmwaBorderColor, ShellBorderColor);
    }

    private static void TrySetColor(IntPtr handle, int attribute, int color)
    {
        _ = DwmSetWindowAttribute(handle, attribute, ref color, sizeof(int));
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
