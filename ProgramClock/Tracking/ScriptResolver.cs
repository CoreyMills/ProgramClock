using System.IO;
using System.Text;

namespace ProgramClock.Tracking;

/// <summary>Maps console shells/hosts to the script they're running so usage lands on the script
/// itself (e.g. "deploy.bat") rather than the generic host that executes it (cmd.exe / conhost.exe /
/// WindowsTerminal.exe). Console hosting is indirect, so the host-to-script mapping is best-effort.</summary>
public static class ScriptResolver
{
    private static readonly string[] ScriptExts = { ".bat", ".cmd", ".ps1" };

    public static bool IsScriptHost(string name) =>
        name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase);

    public static bool IsConsoleHost(string name) =>
        name.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("OpenConsole.exe", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>If this shell process is running a script, build an AppInfo keyed by the script file.</summary>
    public static AppInfo? FromShell(CommandLineReader.ProcInfo shell)
    {
        if (!IsScriptHost(shell.Name)) return null;
        var script = ExtractScriptPath(shell.CommandLine);
        return script is null ? null : AppInfo.ForScript(script, shell.Name);
    }

    /// <summary>Given a console-host PID (conhost/WT/OpenConsole) and a process snapshot, find the
    /// script the associated shell is running. conhost is a child of its shell; Windows Terminal
    /// hosts shells as descendants and may have several tabs, so this returns the first match.</summary>
    public static AppInfo? FromConsoleHost(int hostPid,
        IReadOnlyDictionary<int, CommandLineReader.ProcInfo> snap)
    {
        if (!snap.TryGetValue(hostPid, out var host)) return null;

        // conhost.exe's parent is the shell that owns its console window.
        if (host.Name.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase) &&
            snap.TryGetValue(host.ParentPid, out var parent) &&
            FromShell(parent) is { } fromParent)
            return fromParent;

        // Windows Terminal / OpenConsole: scan for a descendant shell running a script.
        return snap.Values
            .Where(p => IsScriptHost(p.Name) && IsDescendantOf(p, hostPid, snap))
            .Select(FromShell)
            .FirstOrDefault(a => a is not null);
    }

    private static bool IsDescendantOf(CommandLineReader.ProcInfo p, int ancestorPid,
        IReadOnlyDictionary<int, CommandLineReader.ProcInfo> snap)
    {
        int cur = p.ParentPid, guard = 0;
        while (cur != 0 && guard++ < 16)
        {
            if (cur == ancestorPid) return true;
            if (!snap.TryGetValue(cur, out var parent)) break;
            cur = parent.ParentPid;
        }
        return false;
    }

    private static string? ExtractScriptPath(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        foreach (var token in Tokenize(commandLine))
        {
            var ext = Path.GetExtension(token);
            if (ScriptExts.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                return token;
        }
        return null;
    }

    // Minimal command-line splitter that honors double-quoted spans.
    private static IEnumerable<string> Tokenize(string s)
    {
        var sb = new StringBuilder();
        bool inQuotes = false;
        foreach (var ch in s)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (ch == ' ' && !inQuotes)
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
            }
            else sb.Append(ch);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
}
