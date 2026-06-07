using System.Management;

namespace ProgramClock.Tracking;

/// <summary>Snapshots running processes' command lines via WMI so we can see which script a shell
/// (cmd.exe / powershell.exe) is executing. The query is comparatively expensive, so results are
/// cached for a couple of seconds and shared by the run and focus probes.</summary>
public static class CommandLineReader
{
    public readonly record struct ProcInfo(int Pid, int ParentPid, string Name, string? CommandLine);

    private static readonly object Gate = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(2);
    private static Dictionary<int, ProcInfo> _cache = new();
    private static DateTime _cachedAtUtc = DateTime.MinValue;

    public static IReadOnlyDictionary<int, ProcInfo> Snapshot()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - _cachedAtUtc < Ttl) return _cache;

            var map = new Dictionary<int, ProcInfo>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, ParentProcessId, Name, CommandLine FROM Win32_Process");
                foreach (ManagementBaseObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        int pid = ToInt(mo["ProcessId"]);
                        if (pid == 0) continue;
                        map[pid] = new ProcInfo(
                            pid,
                            ToInt(mo["ParentProcessId"]),
                            mo["Name"] as string ?? "",
                            mo["CommandLine"] as string);
                    }
                }
            }
            catch
            {
                // WMI can be unavailable or briefly locked; keep serving the last good snapshot.
            }

            _cache = map;
            _cachedAtUtc = DateTime.UtcNow;
            return _cache;
        }
    }

    private static int ToInt(object? o) => o is null ? 0 : Convert.ToInt32(o);
}
