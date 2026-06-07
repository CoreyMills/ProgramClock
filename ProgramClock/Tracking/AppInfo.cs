using System.Diagnostics;
using System.IO;

namespace ProgramClock.Tracking;

/// <summary>Resolved identity of a tracked application.</summary>
public sealed record AppInfo(string ExeName, string? ExePath, string? DisplayName, string? Publisher)
{
    private static readonly Dictionary<int, AppInfo> Cache = new();

    /// <summary>
    /// Resolve an AppInfo from a process, caching by PID. Returns null when the process has
    /// no usable executable name (e.g. access denied on protected system processes).
    /// </summary>
    public static AppInfo? FromProcess(Process p)
    {
        try
        {
            if (Cache.TryGetValue(p.Id, out var cached)) return cached;

            string exeName = p.ProcessName + ".exe";
            string? path = null, display = null, publisher = null;
            try
            {
                path = p.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var fvi = FileVersionInfo.GetVersionInfo(path);
                    display = First(fvi.FileDescription, fvi.ProductName);
                    publisher = First(fvi.CompanyName);
                    exeName = Path.GetFileName(path);
                }
            }
            catch
            {
                // MainModule throws for processes we can't open; fall back to ProcessName.
            }

            display ??= p.ProcessName;
            var info = new AppInfo(exeName, path, display, publisher);
            Cache[p.Id] = info;
            return info;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Build an AppInfo for a script (.bat/.cmd/.ps1), keyed by the script's file name so its
    /// usage is attributed to the script itself rather than the shell that runs it.</summary>
    public static AppInfo ForScript(string scriptPath, string hostExe)
    {
        var name = Path.GetFileName(scriptPath);
        return new AppInfo(name, scriptPath, name, $"Script ({hostExe})");
    }

    public static void Forget(int pid) => Cache.Remove(pid);

    private static string? First(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();
}
