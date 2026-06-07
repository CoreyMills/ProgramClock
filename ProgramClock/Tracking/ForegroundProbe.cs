using System.Diagnostics;
using ProgramClock.Interop;

namespace ProgramClock.Tracking;

/// <summary>Reads the currently focused application and the user's idle time.</summary>
public static class ForegroundProbe
{
    /// <summary><see cref="Host"/> is the website (e.g. "youtube.com") showing in the focused browser
    /// tab, or null when the foreground app isn't a recognized browser or its URL can't be read. It
    /// lets focused time be split per-site under the browser entry.</summary>
    public readonly record struct Snapshot(AppInfo? App, long IdleMs, string? Host = null);

    public static Snapshot Capture()
    {
        var idle = NativeMethods.GetIdleMilliseconds();
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return new Snapshot(null, idle);

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return new Snapshot(null, idle);

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            var name = proc.ProcessName + ".exe";

            // A foreground UWP app's window is owned by ApplicationFrameHost; resolve the hosted app so
            // focused time lands on the real app (matching ProcessProbe), never the frame host itself.
            if (string.Equals(proc.ProcessName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            {
                var hostedPid = NativeMethods.GetHostedCoreWindowProcessId(hwnd);
                if (hostedPid != 0 && hostedPid != pid)
                {
                    try
                    {
                        using var hosted = Process.GetProcessById((int)hostedPid);
                        return new Snapshot(AppInfo.FromProcess(hosted), idle);
                    }
                    catch { /* hosted app exited */ }
                }
                return new Snapshot(null, idle);
            }

            // Only pay for the WMI command-line snapshot when a console window is in front, then map
            // it to the script the shell is running (best-effort) instead of the generic host exe.
            if (ScriptResolver.IsConsoleHost(name) || ScriptResolver.IsScriptHost(name))
            {
                var snap = CommandLineReader.Snapshot();
                var script = ScriptResolver.IsScriptHost(name) && snap.TryGetValue((int)pid, out var self)
                    ? ScriptResolver.FromShell(self)
                    : ScriptResolver.FromConsoleHost((int)pid, snap);
                if (script is not null) return new Snapshot(script, idle);
            }

            var app = AppInfo.FromProcess(proc);
            // For a browser, also resolve the active tab's website so its focused time can be broken
            // down per-site under the browser entry. Run time stays whole on the browser exe.
            var host = app is not null && Browsers.IsBrowser(app.ExeName)
                ? BrowserUrlReader.GetActiveHost(hwnd)
                : null;
            return new Snapshot(app, idle, host);
        }
        catch
        {
            return new Snapshot(null, idle);
        }
    }
}
