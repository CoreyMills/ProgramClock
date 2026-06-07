using System.Diagnostics;
using ProgramClock.Interop;

namespace ProgramClock.Tracking;

/// <summary>Enumerates user-facing processes (those with a visible main window).</summary>
public static class ProcessProbe
{
    /// <summary>
    /// Returns one AppInfo per distinct executable that currently has at least one process
    /// with a visible main window. System services without a window are skipped.
    /// </summary>
    public static IReadOnlyCollection<AppInfo> CaptureUserFacing()
    {
        var snap = CommandLineReader.Snapshot();
        var byExe = new Dictionary<string, AppInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                // Require a genuinely visible, non-zero-size top-level window. MainWindowHandle alone
                // is non-zero even for hidden windows, which let tray-only helpers, the UWP frame host
                // (ApplicationFrameHost), input hosts (TextInputHost) and suspended Store apps slip in.
                if (!NativeMethods.IsRealVisibleWindow(p.MainWindowHandle)) continue;

                // ApplicationFrameHost owns the visible frame of whatever UWP app is on screen, so it
                // passes the check above under its own meaningless name. Attribute the time to the real
                // hosted app (the CoreWindow's process) instead, and never record the frame host itself.
                if (string.Equals(p.ProcessName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                {
                    var hostedPid = NativeMethods.GetHostedCoreWindowProcessId(p.MainWindowHandle);
                    if (hostedPid == 0 || (int)hostedPid == p.Id) continue;
                    try
                    {
                        using var hosted = Process.GetProcessById((int)hostedPid);
                        if (AppInfo.FromProcess(hosted) is { } hostedInfo)
                            byExe[hostedInfo.ExeName] = hostedInfo;
                    }
                    catch { /* hosted app exited mid-enumeration */ }
                    continue;
                }

                AppInfo? info = null;
                // A console window belongs to a generic host (conhost/WT) or a shell; attribute it to
                // the script the shell is running, when there is one, instead of the host exe.
                if (snap.TryGetValue(p.Id, out var proc))
                {
                    if (ScriptResolver.IsConsoleHost(proc.Name))
                        info = ScriptResolver.FromConsoleHost(p.Id, snap);
                    else if (ScriptResolver.IsScriptHost(proc.Name))
                        info = ScriptResolver.FromShell(proc);
                }

                info ??= AppInfo.FromProcess(p);
                if (info is null) continue;
                byExe[info.ExeName] = info;
            }
            catch
            {
                // Process may exit mid-enumeration; ignore.
            }
            finally
            {
                p.Dispose();
            }
        }

        // Catch script shells whose console is hosted elsewhere (so they have no window of their own),
        // so run time still accrues to the script while it executes.
        foreach (var proc in snap.Values)
            if (ScriptResolver.FromShell(proc) is { } script)
                byExe[script.ExeName] = script;

        return byExe.Values;
    }
}
