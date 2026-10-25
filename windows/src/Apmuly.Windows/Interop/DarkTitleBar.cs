using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Apmuly.Windows.Interop;

/// <summary>Asks DWM for a dark title bar (Windows 10 20H1+ / 11); ignored where unsupported.</summary>
internal static partial class DarkTitleBar
{
    private const int UseImmersiveDarkMode = 20;

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }
        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
