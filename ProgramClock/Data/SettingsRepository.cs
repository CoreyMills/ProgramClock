using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ProgramClock.Data;

/// <summary>Persisted dashboard window placement (device-independent units).</summary>
public readonly record struct WindowBounds(
    double Left, double Top, double Width, double Height, bool Maximized);

/// <summary>Key-value access to the settings table.</summary>
public sealed class SettingsRepository
{
    public const string IdleThresholdSecondsKey = "idle_threshold_seconds";
    public const int DefaultIdleThresholdSeconds = 180;

    public const string FocusRefreshSecondsKey = "focus_refresh_seconds";
    public const int DefaultFocusRefreshSeconds = 1;

    public const string RunRefreshSecondsKey = "run_refresh_seconds";
    public const int DefaultRunRefreshSeconds = 5;

    // Dashboard keyboard shortcuts: the WPF Key name (e.g. "Delete", "B") applied to the current
    // selection. An empty value means the action is unbound. Delete defaults to the Delete key; Block
    // is unbound by default so it never fires unexpectedly until the user assigns a key.
    public const string DeleteHotkeyKey = "delete_hotkey";
    public const string DefaultDeleteHotkey = "Delete";

    public const string BlockHotkeyKey = "block_hotkey";
    public const string DefaultBlockHotkey = "";

    private readonly SqliteConnection _conn;

    public SettingsRepository(SqliteConnection conn)
    {
        _conn = conn;
        if (Get(IdleThresholdSecondsKey) is null)
            Set(IdleThresholdSecondsKey, DefaultIdleThresholdSeconds.ToString());
        if (Get(FocusRefreshSecondsKey) is null)
            Set(FocusRefreshSecondsKey, DefaultFocusRefreshSeconds.ToString());
        if (Get(RunRefreshSecondsKey) is null)
            Set(RunRefreshSecondsKey, DefaultRunRefreshSeconds.ToString());
    }

    public string? Get(string key)
    {
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE key=$k;";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }
    }

    public void Set(string key, string value)
    {
        lock (_conn)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO settings(key,value) VALUES($k,$v) " +
                "ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", value);
            cmd.ExecuteNonQuery();
        }
    }

    public int GetIdleThresholdSeconds()
    {
        var raw = Get(IdleThresholdSecondsKey);
        return int.TryParse(raw, out var v) && v > 0 ? v : DefaultIdleThresholdSeconds;
    }

    public void SetIdleThresholdSeconds(int seconds) =>
        Set(IdleThresholdSecondsKey, Math.Max(1, seconds).ToString());

    public int GetFocusRefreshSeconds()
    {
        var raw = Get(FocusRefreshSecondsKey);
        return int.TryParse(raw, out var v) && v > 0 ? v : DefaultFocusRefreshSeconds;
    }

    public void SetFocusRefreshSeconds(int seconds) =>
        Set(FocusRefreshSecondsKey, Math.Max(1, seconds).ToString());

    public int GetRunRefreshSeconds()
    {
        var raw = Get(RunRefreshSecondsKey);
        return int.TryParse(raw, out var v) && v > 0 ? v : DefaultRunRefreshSeconds;
    }

    public void SetRunRefreshSeconds(int seconds) =>
        Set(RunRefreshSecondsKey, Math.Max(1, seconds).ToString());

    /// <summary>The key bound to Delete-selection (WPF Key name), or empty if unbound.</summary>
    public string GetDeleteHotkey() => Get(DeleteHotkeyKey) ?? DefaultDeleteHotkey;
    public void SetDeleteHotkey(string keyName) => Set(DeleteHotkeyKey, keyName.Trim());

    /// <summary>The key bound to Block-selection (WPF Key name), or empty if unbound.</summary>
    public string GetBlockHotkey() => Get(BlockHotkeyKey) ?? DefaultBlockHotkey;
    public void SetBlockHotkey(string keyName) => Set(BlockHotkeyKey, keyName.Trim());

    // Auto-update: opt-in. Defaults OFF (not seeded) so the app never reaches the network
    // unless the user explicitly turns this on. The manual "Check for Updates" / "Update Now"
    // buttons work regardless of this setting.
    public const string AutoUpdateEnabledKey = "auto_update_enabled";
    public bool GetAutoUpdateEnabled() => Get(AutoUpdateEnabledKey) == "1";
    public void SetAutoUpdateEnabled(bool on) => Set(AutoUpdateEnabledKey, on ? "1" : "0");

    // Last date (local yyyy-MM-dd) an automatic update check ran, so the daily check fires at most once a day.
    public const string LastUpdateCheckKey = "last_update_check";
    public string? GetLastUpdateCheckDate() => Get(LastUpdateCheckKey);
    public void SetLastUpdateCheckDate(string date) => Set(LastUpdateCheckKey, date);

    // User-chosen accent colour (hex, e.g. "#D61414"). Defaults to the app's signature red; when left
    // at the default the per-theme built-in red is used unchanged.
    public const string AccentColorKey = "accent_color";
    public const string DefaultAccentColor = "#D61414";
    public string GetAccentColor() => Get(AccentColorKey) ?? DefaultAccentColor;
    public void SetAccentColor(string hex) => Set(AccentColorKey, hex.Trim());

    // User-chosen main (window background) colour. Empty means "follow the Windows light/dark theme";
    // any hex value overrides the background and derives a matching text/surface/border palette.
    public const string MainColorKey = "main_color";
    public string GetMainColor() => Get(MainColorKey) ?? "";
    public void SetMainColor(string hex) => Set(MainColorKey, hex.Trim());

    // The user's normal day window (local 24-hour "HH:mm"). Running time only starts counting once
    // the user first focuses an app each day; outside this window, a long lock stops it (see below).
    public const string DayStartKey = "day_start_time";
    public const string DefaultDayStart = "08:00";
    public string GetDayStart() => Get(DayStartKey) ?? DefaultDayStart;
    public void SetDayStart(string hhmm) => Set(DayStartKey, hhmm.Trim());

    public const string DayEndKey = "day_end_time";
    public const string DefaultDayEnd = "22:00";
    public string GetDayEnd() => Get(DayEndKey) ?? DefaultDayEnd;
    public void SetDayEnd(string hhmm) => Set(DayEndKey, hhmm.Trim());

    // When enabled, running time stops accruing once the PC has been locked for at least this many
    // minutes AND the current time is outside the day window. Unlocking resumes it immediately.
    public const string LockStopEnabledKey = "lock_stop_enabled";
    public bool GetLockStopEnabled() => (Get(LockStopEnabledKey) ?? "1") == "1";
    public void SetLockStopEnabled(bool on) => Set(LockStopEnabledKey, on ? "1" : "0");

    public const string LockStopMinutesKey = "lock_stop_minutes";
    public const int DefaultLockStopMinutes = 15;
    public int GetLockStopMinutes()
    {
        var raw = Get(LockStopMinutesKey);
        return int.TryParse(raw, out var v) && v >= 0 ? v : DefaultLockStopMinutes;
    }
    public void SetLockStopMinutes(int minutes) => Set(LockStopMinutesKey, Math.Max(0, minutes).ToString());

    // How long the pointer must rest on a chart section before its describing tooltip appears (ms).
    public const string TooltipDelayMsKey = "tooltip_delay_ms";
    public const int DefaultTooltipDelayMs = 250;
    public int GetTooltipDelayMs()
    {
        var raw = Get(TooltipDelayMsKey);
        return int.TryParse(raw, out var v) && v >= 0 ? v : DefaultTooltipDelayMs;
    }
    public void SetTooltipDelayMs(int ms) => Set(TooltipDelayMsKey, Math.Max(0, ms).ToString());

    // Whether the hovered chart section grows/pops out. Defaults on.
    public const string GrowOnHoverKey = "grow_on_hover";
    public bool GetGrowOnHover() => (Get(GrowOnHoverKey) ?? "1") == "1";
    public void SetGrowOnHover(bool on) => Set(GrowOnHoverKey, on ? "1" : "0");

    // The dashboard's last-used range, restored on the next launch (defaults to Today).
    public const string LastRangeKey = "last_range";
    public DateRange GetLastRange() => Enum.TryParse<DateRange>(Get(LastRangeKey), out var r) ? r : DateRange.Today;
    public void SetLastRange(DateRange range) => Set(LastRangeKey, range.ToString());

    // The dashboard's last-used view as an enum name (e.g. "Table"/"Donut"); the UI parses it back.
    public const string LastViewKey = "last_view";
    public string? GetLastView() => Get(LastViewKey);
    public void SetLastView(string viewName) => Set(LastViewKey, viewName);

    // The app version last seen by the user, used to show the patch-notes popup once per update.
    public const string LastSeenVersionKey = "last_seen_version";
    public string? GetLastSeenVersion() => Get(LastSeenVersionKey);
    public void SetLastSeenVersion(string version) => Set(LastSeenVersionKey, version);

    // Personalizable tag labels/weights (keyed per enum value, e.g. "tag_name_Main") plus the
    // user-defined efficiency ratings (stored as a JSON list under one key).
    private const string TagNamePrefix = "tag_name_";
    private const string TagWeightPrefix = "tag_weight_";
    private const string EfficiencyRatingsKey = "efficiency_ratings";

    // Pre-list (per-tier) keys, read only when migrating an older database to the JSON ratings list.
    private const string TierNamePrefix = "tier_name_";
    private const string TierThresholdPrefix = "tier_threshold_";

    /// <summary>Load the user's tag labels/weights and efficiency ratings, falling back to the
    /// built-in defaults for anything the user hasn't customised.</summary>
    public EfficiencySettings GetEfficiencySettings()
    {
        var names = new Dictionary<AppTag, string>();
        var weights = new Dictionary<AppTag, double>();
        foreach (var t in EfficiencySettings.Tags)
        {
            names[t] = Get(TagNamePrefix + t) ?? EfficiencySettings.DefaultTagName(t);
            var raw = Get(TagWeightPrefix + t);
            weights[t] =
                double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w >= 0
                    ? w
                    : EfficiencySettings.DefaultWeight(t);
        }

        return new EfficiencySettings(names, weights, LoadRatings());
    }

    public void SaveEfficiencySettings(
        IReadOnlyDictionary<AppTag, string> tagNames,
        IReadOnlyDictionary<AppTag, double> tagWeights,
        IReadOnlyList<EfficiencyRating> ratings)
    {
        foreach (var t in EfficiencySettings.Tags)
        {
            Set(TagNamePrefix + t, tagNames[t]);
            Set(TagWeightPrefix + t, tagWeights[t].ToString(CultureInfo.InvariantCulture));
        }
        Set(EfficiencyRatingsKey, JsonSerializer.Serialize(ratings));
    }

    private IReadOnlyList<EfficiencyRating> LoadRatings()
    {
        var raw = Get(EfficiencyRatingsKey);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<EfficiencyRating>>(raw);
                if (list is { Count: > 0 }) return list;
            }
            catch { /* corrupt value: fall back to migration/defaults below */ }
        }
        return MigrateLegacyRatings();
    }

    // Build the ratings list from the pre-list per-tier settings so any earlier custom names or
    // thresholds survive, falling back to the built-in defaults for anything not previously set.
    private IReadOnlyList<EfficiencyRating> MigrateLegacyRatings()
    {
        var legacy = new (string Key, string Name, double Threshold)[]
        {
            ("Poor", "Poor", 0.0),
            ("Fair", "Fair", 0.40),
            ("Good", "Good", 0.60),
            ("Excellent", "Excellent", 0.80),
        };

        var list = new List<EfficiencyRating>();
        foreach (var (key, defName, defThreshold) in legacy)
        {
            var name = Get(TierNamePrefix + key) ?? defName;
            var rawThreshold = Get(TierThresholdPrefix + key);
            var threshold =
                double.TryParse(rawThreshold, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0
                    ? v
                    : defThreshold;
            list.Add(new EfficiencyRating(name, threshold));
        }
        return list;
    }

    public const string WindowBoundsKey = "window_bounds";

    /// <summary>The last saved dashboard placement, or null if none has been saved yet.</summary>
    public WindowBounds? GetWindowBounds()
    {
        var raw = Get(WindowBoundsKey);
        if (raw is null) return null;

        var p = raw.Split(',');
        if (p.Length != 5) return null;

        var c = CultureInfo.InvariantCulture;
        if (double.TryParse(p[0], NumberStyles.Float, c, out var left) &&
            double.TryParse(p[1], NumberStyles.Float, c, out var top) &&
            double.TryParse(p[2], NumberStyles.Float, c, out var width) &&
            double.TryParse(p[3], NumberStyles.Float, c, out var height) &&
            width > 0 && height > 0)
        {
            return new WindowBounds(left, top, width, height, p[4] == "1");
        }
        return null;
    }

    public void SetWindowBounds(WindowBounds b)
    {
        var c = CultureInfo.InvariantCulture;
        Set(WindowBoundsKey, string.Join(',',
            b.Left.ToString(c), b.Top.ToString(c),
            b.Width.ToString(c), b.Height.ToString(c),
            b.Maximized ? "1" : "0"));
    }
}
