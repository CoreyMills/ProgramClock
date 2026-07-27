namespace ProgramClock.UI;

/// <summary>Copy shown in the one-time popups: the first-run introduction and the post-update patch
/// notes. Update <see cref="PatchNotes"/> each release to describe that version's changes.</summary>
public static class InfoContent
{
    /// <summary>Bulleted changes for the current release, each noting where to find the feature.</summary>
    public static readonly IReadOnlyList<string> PatchNotes = new[]
    {
        "Categories can now have a default tag (set it on the Categories page). Apps moved into that category adopt the tag automatically — but only if you haven't already given the app a tag of your own.",
        "New apps are now categorized immediately when first detected, instead of after a short delay.",
        "You can set a minimum window width and height in Settings → General (use 0 for no limit, so you can resize the window freely).",
        "The top bar now wraps its controls onto another line when the window is narrow, instead of clipping them.",
        "The Minimal view's big figures now scale down to fit a smaller window.",
        "Fixed: setting a brand-new app's category from the table no longer snaps back to Uncategorized.",
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
