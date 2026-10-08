using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VortexArena.Launcher.Infrastructure;

/// <summary>Win32 touches the launcher needs: dark title bar and bringing the first window up.</summary>
internal static class NativeMethods
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    private const int SwRestore = 9;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    /// <summary>Paints the title bar dark. Silent on older Windows — the app is still usable.</summary>
    public static void ApplyDarkTitleBar(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int enabled = 1;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        }
        catch (Exception)
        {
            // Attribute unsupported; cosmetic only.
        }
    }

    /// <summary>Un-minimizes and focuses the window (second launch hands control to the first).</summary>
    public static void Activate(Window window)
    {
        try
        {
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Show();
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            ShowWindow(handle, SwRestore);
            SetForegroundWindow(handle);
        }
        catch (Exception)
        {
            // Activation is best effort.
        }
    }
}
