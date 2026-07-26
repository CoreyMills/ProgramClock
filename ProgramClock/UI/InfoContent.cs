namespace ProgramClock.UI;

/// <summary>Copy shown in the one-time popups: the first-run introduction and the post-update patch
/// notes. Update <see cref="PatchNotes"/> each release to describe that version's changes.</summary>
public static class InfoContent
{
    /// <summary>Bulleted changes for the current release, each noting where to find the feature.</summary>
    public static readonly IReadOnlyList<string> PatchNotes = new[]
    {
        "Edit an app's category and tag directly in the dashboard table — use the dropdowns in the Category and Tag columns; changes apply immediately.",
        "Right-click an app → \"Reset data (current range)\" to clear just that app's tracked time for the range you're viewing.",
        "New \"Clear range data\" button in the top bar clears every app's time for the current range.",
        "The window now has a minimum size, so the top-bar controls can no longer be shrunk out of view.",
        "The dashboard remembers your last Range and View between runs.",
    };

    /// <summary>General how-to shown once on a fresh install.</summary>
    public static readonly IReadOnlyList<string> Welcome = new[]
    {
        "ProgramClock tracks, per app, how long it runs and how long you're actively focused in it — all stored locally on your PC, nothing is sent anywhere.",
        "It lives in the system tray: click the tray icon to open this dashboard; right-click it for quick actions and to quit.",
        "Use the Range selector (Today / Week / Month / All) and the View selector (Table, Bar, Donut, Trend) in the top bar to explore your usage.",
        "Set an app's category and tag right in the table; create and manage categories on the Categories page.",
        "Tune behaviour in Settings — idle timeout, day hours, chart options, keyboard shortcuts and efficiency ratings.",
        "Updates are opt-in: turn on the daily check, or use \"Check for Updates\" in Settings → Updates.",
    };
}
