using System.Runtime.InteropServices;

namespace ProgramClock.Interop;

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    /// <summary>For an ApplicationFrameHost frame window, find the hosted UWP app's process. The real
    /// app owns a "Windows.UI.Core.CoreWindow" child of the frame; the frame host is just a shell that
    /// draws the title bar. Returns 0 when there's no such child (nothing real to attribute to).</summary>
    internal static uint GetHostedCoreWindowProcessId(IntPtr frameHwnd)
    {
        var core = FindWindowEx(frameHwnd, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
        if (core == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(core, out var pid);
        return pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    // Ask DWM whether a window is "cloaked" — hidden by the compositor while keeping its WS_VISIBLE
    // style. This is how Windows backgrounds UWP/Store apps (SystemSettings, Video.UI), the UWP frame
    // host (ApplicationFrameHost), input hosts (TextInputHost), and windows on other virtual desktops.
    // IsWindowVisible still returns true for these, so cloaking is the only reliable way to skip them.
    private const int DWMWA_CLOAKED = 14;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    private static bool IsCloaked(IntPtr hWnd) =>
        DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>True when the window is genuinely on-screen: shown (WS_VISIBLE), not DWM-cloaked, and
    /// of a real non-zero size. Filters out processes that own only a hidden, cloaked, or 0×0 top-level
    /// window — tray-only helpers, the UWP frame host, input hosts, and suspended Store apps — all of
    /// which still report a non-zero MainWindowHandle.</summary>
    internal static bool IsRealVisibleWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !IsWindowVisible(hWnd) || IsCloaked(hWnd)) return false;
        if (!GetWindowRect(hWnd, out var r)) return false;
        return r.Right - r.Left > 0 && r.Bottom - r.Top > 0;
    }

    /// <summary>Milliseconds since the last keyboard/mouse input across the session.</summary>
    internal static long GetIdleMilliseconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        // Environment.TickCount wraps ~24.9 days; unchecked subtraction handles wrap.
        return unchecked((uint)Environment.TickCount - info.dwTime);
    }
}
