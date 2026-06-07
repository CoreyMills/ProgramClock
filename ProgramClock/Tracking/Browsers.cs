namespace ProgramClock.Tracking;

/// <summary>The set of executables treated as web browsers, whose focused time is further broken
/// down by the website (host) showing in the active tab. Only the active tab can be observed, so
/// this split applies to focused time only — run time stays attributed to the browser exe itself.</summary>
internal static class Browsers
{
    private static readonly HashSet<string> Exes = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe",
        "msedge.exe",
        "firefox.exe",
        "brave.exe",
        "opera.exe",
        "opera_gx.exe",
        "vivaldi.exe",
    };

    public static bool IsBrowser(string? exeName) =>
        exeName is not null && Exes.Contains(exeName);
}
