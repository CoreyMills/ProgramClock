namespace ProgramClock.UI;

/// <summary>Copy shown in the one-time popups: the first-run introduction and the post-update patch
/// notes. Update <see cref="PatchNotes"/> each release to describe that version's changes.</summary>
public static class InfoContent
{
    /// <summary>Bulleted changes for the current release, each noting where to find the feature.</summary>
    public static readonly IReadOnlyList<string> PatchNotes = new[]
    {
        "New \"Minimal\" view (in the View selector) shows just two big figures — Focused, and Unfocused (in-use-but-not-focused time) — for an at-a-glance read of your day.",
        "Fixed: the dashboard table's category and tag dropdowns now commit reliably when you pick an option.",
        "Fixed: apps set to \"Uncategorized\" now show that in the table instead of appearing blank, including after visiting the Categories page.",
        "Re-open this Patch Notes popup or the Introduction any time from Settings → General → Help.",
        "Right-click → \"Reset data (current range)\" now clears both focused and running time for that app.",
    };

    /// <summary>General how-to shown once on a fresh install.</summary>
    public static readonly IReadOnlyList<string> Welcome = new[]
    {
        "ProgramClock tracks, per app, how long it runs and how long you're actively focused in it — all stored locally on your PC, nothing is sent anywhere.",
        "It lives in the system tray: click the tray icon to open this dashboard; right-click it for quick actions and to quit.",
        "Use the Range selector (Today / Week / Month / All) and the View selector (Table, Bar, Donut, Trend, Minimal) in the top bar to explore your usage.",
        "Set an app's category and tag right in the table; create and manage categories on the Categories page.",
        "Tune behaviour in Settings — idle timeout, day hours, chart options, keyboard shortcuts and efficiency ratings.",
        "Updates are opt-in: turn on the daily check, or use \"Check for Updates\" in Settings → Updates.",
    };
}
