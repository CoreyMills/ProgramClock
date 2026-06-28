using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using ProgramClock.Data;
using ProgramClock.Tracking;

namespace ProgramClock.UI;

/// <summary>One dashboard row. Mutable and observable so <see cref="DashboardViewModel.Reload"/> can
/// update values in place each refresh tick instead of replacing the row objects — that keeps the
/// grid's selection (and its shift/drag anchor) alive across refreshes. <see cref="ExeName"/> is the
/// immutable key that identifies a row across reloads.</summary>
public sealed class UsageRowVm : INotifyPropertyChanged
{
    private readonly Func<AppTag, string> _tagName;

    public UsageRowVm(string exeName, Func<AppTag, string> tagName)
    {
        ExeName = exeName;
        _tagName = tagName;
    }

    public string ExeName { get; }

    /// <summary>Per-website focused-time breakdown shown in this row's expandable detail area. Empty
    /// for non-browsers (see <see cref="HasSites"/>).</summary>
    public ObservableCollection<SiteRowVm> Sites { get; } = new();

    private bool _hasSites;
    /// <summary>True when this app is a browser with at least one tracked website, so the row offers
    /// an expandable site breakdown.</summary>
    public bool HasSites
    {
        get => _hasSites;
        private set
        {
            if (!Set(ref _hasSites, value)) return;
            OnChanged(nameof(SitesVisibility));
            OnChanged(nameof(SitesDetailsVisibility));
        }
    }

    /// <summary>Drives the disclosure toggle: shown only on browser rows that have websites.</summary>
    public Visibility SitesVisibility => HasSites ? Visibility.Visible : Visibility.Collapsed;

    private bool _isExpanded;
    /// <summary>Whether this row's website breakdown is open. Toggled by the disclosure control and
    /// kept independent of row selection, so the list stays open until the user closes it. Closing it
    /// clears any manual website selection (the dropdown reopens with nothing selected) — unless the
    /// browser row itself is selected, in which case its websites stay selected with it.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!Set(ref _isExpanded, value)) return;
            OnChanged(nameof(SitesDetailsVisibility));
            if (!value && !IsRowSelected)
                foreach (var s in Sites) s.IsSelected = false;
        }
    }

    private bool _isRowSelected;
    /// <summary>Two-way bound to this row's grid container. Selecting a browser row selects all of its
    /// websites too (deleting/blocking the row would take them with it); deselecting clears them.</summary>
    public bool IsRowSelected
    {
        get => _isRowSelected;
        set
        {
            if (!Set(ref _isRowSelected, value)) return;
            foreach (var s in Sites) s.IsSelected = value;
        }
    }

    /// <summary>The expanded site list is visible only when this browser row is both expanded and has
    /// at least one website.</summary>
    public Visibility SitesDetailsVisibility =>
        IsExpanded && HasSites ? Visibility.Visible : Visibility.Collapsed;


    /// <summary>Replace the site breakdown in place (keyed by host) so the expanded list updates each
    /// refresh tick without losing scroll position or flickering.</summary>
    public void UpdateSites(IReadOnlyDictionary<string, long> sites)
    {
        for (int i = Sites.Count - 1; i >= 0; i--)
            if (!sites.ContainsKey(Sites[i].Host))
                Sites.RemoveAt(i);

        var existing = new Dictionary<string, SiteRowVm>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in Sites) existing[s.Host] = s;

        foreach (var kv in sites.OrderByDescending(kv => kv.Value))
        {
            if (!existing.TryGetValue(kv.Key, out var site))
            {
                // A website appearing under an already-selected browser row inherits its selection,
                // so it's deleted/blocked along with the row.
                site = new SiteRowVm(ExeName, kv.Key) { IsSelected = IsRowSelected };
                Sites.Add(site);
            }
            site.FocusMs = kv.Value;
        }
        HasSites = Sites.Count > 0;
    }

    private string _displayName = "";
    public string DisplayName { get => _displayName; set => Set(ref _displayName, value); }

    private long _runMs;
    public long RunMs { get => _runMs; set { if (Set(ref _runMs, value)) OnChanged(nameof(RunTime)); } }

    private long _focusMs;
    public long FocusMs { get => _focusMs; set { if (Set(ref _focusMs, value)) OnChanged(nameof(FocusTime)); } }

    private string _categoryName = "";
    public string CategoryName { get => _categoryName; set => Set(ref _categoryName, value); }

    private AppTag _tag = AppTag.Other;
    public AppTag Tag { get => _tag; set { if (Set(ref _tag, value)) OnChanged(nameof(TagName)); } }

    /// <summary>The tag's user-facing label (resolved live from the current personalization settings).</summary>
    public string TagName => _tagName(Tag);

    /// <summary>Re-raise <see cref="TagName"/> after the user renames tags in settings.</summary>
    public void RefreshTagName() => OnChanged(nameof(TagName));

    public string RunTime => TimeFormat.Humanize(RunMs);
    public string FocusTime => TimeFormat.Humanize(FocusMs);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name!);
        return true;
    }
}

/// <summary>One website under a browser row, shown in the expandable site breakdown. Mutable so its
/// focused time updates in place each refresh tick. <see cref="Host"/> is the immutable key.</summary>
public sealed class SiteRowVm : INotifyPropertyChanged
{
    public SiteRowVm(string exeName, string host)
    {
        ExeName = exeName;
        Host = host;
    }

    /// <summary>The owning browser's executable name — needed so Delete/Block know which browser the
    /// website belongs to.</summary>
    public string ExeName { get; }

    public string Host { get; }

    private long _focusMs;
    public long FocusMs { get => _focusMs; set { if (Set(ref _focusMs, value)) OnChanged(nameof(FocusTime)); } }

    public string FocusTime => TimeFormat.Humanize(FocusMs);

    private bool _isSelected;
    /// <summary>Two-way bound to the website row's container so the view model can clear the selection
    /// when the dropdown closes, and the drag/click handlers can drive the red highlight.</summary>
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name!);
        return true;
    }
}

public enum DashboardPage { Dashboard, Settings, Categories }

/// <summary>A selectable category for the per-app assignment dropdown. A null Id means "Uncategorized".</summary>
public sealed record CategoryChoice(long? Id, string Name);

/// <summary>A selectable tag for the per-app dropdown: the enum value plus its current display name.</summary>
public sealed record TagChoice(AppTag Tag, string Name);

/// <summary>An editable row in the settings tag editor: the fixed tag identity plus its user-set
/// display name and efficiency weight (kept as a string so the TextBox can hold partial input).</summary>
public sealed class TagLabelVm
{
    public TagLabelVm(AppTag tag, string name, double weight)
    {
        Tag = tag;
        Name = name;
        Weight = weight.ToString(CultureInfo.InvariantCulture);
    }

    public AppTag Tag { get; }
    public string TagKey => Tag.ToString();
    public string Name { get; set; }
    public string Weight { get; set; }
}

/// <summary>An editable row in the settings efficiency editor: a rating's display name and the
/// minimum score (as a percentage) needed to reach it. The user can add or remove rows for more or
/// less granularity; on save the rows are sorted by threshold and the lowest becomes the floor.</summary>
public sealed class RatingLabelVm
{
    public RatingLabelVm(string name, double threshold)
    {
        Name = name;
        ThresholdPercent = (threshold * 100).ToString("0.#", CultureInfo.InvariantCulture);
    }

    public string Name { get; set; }

    /// <summary>Minimum efficiency percentage (0..100) to reach this rating; bound to the editor TextBox.</summary>
    public string ThresholdPercent { get; set; }
}

/// <summary>A category row on the management page. Editing <see cref="Name"/> persists a rename.</summary>
public sealed class CategoryItemVm : INotifyPropertyChanged
{
    private readonly Action<long, string> _rename;
    private string _name;

    public CategoryItemVm(long id, string name, bool isAuto, Action<long, string> rename)
    {
        Id = id;
        _name = name;
        IsAuto = isAuto;
        _rename = rename;
    }

    public long Id { get; }
    public bool IsAuto { get; }
    public string Origin => IsAuto ? "Auto" : "Manual";

    public string Name
    {
        get => _name;
        set
        {
            var v = value?.Trim() ?? "";
            if (v.Length == 0 || v == _name) { OnChanged(); return; }
            _name = v;
            _rename(Id, v);
            OnChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>An app row on the management page. Changing <see cref="CategoryId"/> persists the
/// assignment.</summary>
public sealed class AppCategoryItemVm : INotifyPropertyChanged
{
    private readonly Action<long, long?> _assign;
    private readonly Action<long, AppTag> _assignTag;
    private long? _categoryId;
    private AppTag _tag;

    public AppCategoryItemVm(long appId, string displayName, string exeName, long? categoryId,
        AppTag tag, Action<long, long?> assign, Action<long, AppTag> assignTag)
    {
        AppId = appId;
        DisplayName = displayName;
        ExeName = exeName;
        _categoryId = categoryId;
        _tag = tag;
        _assign = assign;
        _assignTag = assignTag;
    }

    public long AppId { get; }
    public string DisplayName { get; }
    public string ExeName { get; }

    /// <summary>Set true when this row is being torn down (its page is reloading). A ComboBox whose
    /// ItemsSource is cleared writes a null SelectedValue back through its binding; without this guard
    /// that spurious write would persist "Uncategorized"/"Other" over the user's real choice.</summary>
    public bool Detached { get; set; }

    public long? CategoryId
    {
        get => _categoryId;
        set
        {
            if (Detached || _categoryId == value) return;
            _categoryId = value;
            _assign(AppId, value);
            OnChanged();
        }
    }

    public AppTag Tag
    {
        get => _tag;
        set
        {
            if (Detached || _tag == value) return;
            _tag = value;
            _assignTag(AppId, value);
            OnChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class DashboardViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly UsageRepository _usage;
    private readonly UsageTracker _tracker;
    private readonly SettingsRepository _settings;
    private readonly CategoryRepository _categories;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _wheelTimer;
    private EfficiencySettings _labels;
    private DateTime _focusEpochUtc;
    private DateTime _runEpochUtc;
    private double _focusSeconds = 1;
    private double _runSeconds = 5;

    // Shared empty map handed to non-browser rows so they clear any (impossible) leftover site list.
    private static readonly IReadOnlyDictionary<string, long> EmptySites =
        new Dictionary<string, long>();

    public ObservableCollection<UsageRowVm> Rows { get; } = new();
    public Array Ranges => Enum.GetValues<DateRange>();

    /// <summary>While true, timer-driven <see cref="Reload"/> calls are skipped so rebuilding the
    /// grid can't interrupt an in-progress mouse drag-selection (which destroys the row visuals and
    /// the selection anchor mid-gesture). The view sets it on mouse-down in the grid and clears it,
    /// then forces one catch-up Reload, on mouse-up.</summary>
    public bool SuspendRefresh { get; set; }

    private DateRange _selectedRange = DateRange.Today;
    public DateRange SelectedRange
    {
        get => _selectedRange;
        set { if (_selectedRange != value) { _selectedRange = value; OnChanged(); Reload(); } }
    }

    private DashboardPage _page = DashboardPage.Dashboard;
    public DashboardPage Page
    {
        get => _page;
        set
        {
            if (_page == value) return;
            _page = value;
            OnChanged();
            OnChanged(nameof(DashboardVisibility));
            OnChanged(nameof(SettingsVisibility));
            OnChanged(nameof(CategoriesVisibility));
            // Timer-driven reloads are skipped while another page is shown (the grid is collapsed),
            // so refresh once on return so the Dashboard shows current data immediately.
            if (value == DashboardPage.Dashboard) Reload();
        }
    }

    /// <summary>Choices for the per-app dropdown on the categories page (includes "Uncategorized").</summary>
    public ObservableCollection<CategoryChoice> CategoryChoices { get; } = new();
    /// <summary>Tag choices (value + current display name) for the per-app tag dropdown.</summary>
    public ObservableCollection<TagChoice> TagChoices { get; } = new();

    /// <summary>Editable tag display-name/weight rows shown on the settings page.</summary>
    public ObservableCollection<TagLabelVm> TagLabels { get; } = new();
    /// <summary>Editable efficiency-rating rows (name + threshold) shown on the settings page. The
    /// user can add or remove rows for more or less granularity.</summary>
    public ObservableCollection<RatingLabelVm> RatingLabels { get; } = new();
    /// <summary>The editable list of categories on the management page.</summary>
    public ObservableCollection<CategoryItemVm> ManagedCategories { get; } = new();
    /// <summary>Every known app and its current category, for assignment on the management page.</summary>
    public ObservableCollection<AppCategoryItemVm> ManagedApps { get; } = new();

    /// <summary>Exe names the user has blocked from detection, shown on the Settings page.</summary>
    public ObservableCollection<string> BlockedApps { get; } = new();

    /// <summary>Shows the "No blocked apps" placeholder when the blocked list is empty.</summary>
    public Visibility NoBlockedAppsVisibility =>
        BlockedApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Hosts the user has blocked from per-site tracking, shown on the Settings page.</summary>
    public ObservableCollection<string> BlockedSites { get; } = new();

    /// <summary>Shows the "No blocked websites" placeholder when the blocked-sites list is empty.</summary>
    public Visibility NoBlockedSitesVisibility =>
        BlockedSites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DashboardVisibility =>
        Page == DashboardPage.Dashboard ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsVisibility =>
        Page == DashboardPage.Settings ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CategoriesVisibility =>
        Page == DashboardPage.Categories ? Visibility.Visible : Visibility.Collapsed;

    private string _totalRun = "0s";
    public string TotalRun { get => _totalRun; private set { _totalRun = value; OnChanged(); } }

    private string _totalFocus = "0s";
    public string TotalFocus { get => _totalFocus; private set { _totalFocus = value; OnChanged(); } }

    // The range's overall totals (ms), kept so the header can flip back to them when the selection
    // summary turns off. The displayed TotalRun/TotalFocus strings may instead show the selected sum.
    private long _totalRunMs;
    private long _totalFocusMs;
    private readonly List<UsageRowVm> _selectedRows = new();

    // Show the combined times of selected rows once this many are highlighted; below it, show the
    // range total. (One selected row reads fine on its own line, so "combined" starts at two.)
    private const int SelectionSummaryMin = 2;

    private string _focusLabel = "Total Focused: ";
    public string FocusLabel { get => _focusLabel; private set { if (_focusLabel != value) { _focusLabel = value; OnChanged(); } } }

    private string _runLabel = "Total Running: ";
    public string RunLabel { get => _runLabel; private set { if (_runLabel != value) { _runLabel = value; OnChanged(); } } }

    private bool _isUserIdle;
    public bool IsUserIdle
    {
        get => _isUserIdle;
        private set { if (_isUserIdle != value) { _isUserIdle = value; OnChanged(); OnChanged(nameof(IdleStatusText)); } }
    }
    public string IdleStatusText => IsUserIdle ? "Idle" : "Active";

    private string _efficiencyLevel = "None";
    /// <summary>Coarse colour bucket ("None"/"Low"/"Mid"/"High") for how the selected range's focused
    /// time was spent. Drives the colour of the efficiency pill in the dashboard header, independent
    /// of how many ratings the user has defined.</summary>
    public string EfficiencyLevel
    {
        get => _efficiencyLevel;
        private set { if (_efficiencyLevel != value) { _efficiencyLevel = value; OnChanged(); } }
    }

    private string _efficiencyText = "Efficiency: —";
    public string EfficiencyText
    {
        get => _efficiencyText;
        private set { if (_efficiencyText != value) { _efficiencyText = value; OnChanged(); } }
    }

    private string _efficiencyTooltip = "No focused time recorded in this range.";
    public string EfficiencyTooltip
    {
        get => _efficiencyTooltip;
        private set { if (_efficiencyTooltip != value) { _efficiencyTooltip = value; OnChanged(); } }
    }

    private bool _showManualRefresh;
    public bool ShowManualRefresh
    {
        get => _showManualRefresh;
        private set { if (_showManualRefresh != value) { _showManualRefresh = value; OnChanged(); OnChanged(nameof(ManualRefreshVisibility)); } }
    }
    public Visibility ManualRefreshVisibility =>
        ShowManualRefresh ? Visibility.Visible : Visibility.Collapsed;

    // ---- Auto-update state (settings page) ----

    private string _updateStatusText = "";
    /// <summary>Status line under the update buttons (e.g. "Checking…", "Up to date").</summary>
    public string UpdateStatusText
    {
        get => _updateStatusText;
        set { if (_updateStatusText != value) { _updateStatusText = value; OnChanged(); } }
    }

    private bool _updateControlsEnabled = true;
    /// <summary>False while a check/download is in flight, to disable the update buttons.</summary>
    public bool UpdateControlsEnabled
    {
        get => _updateControlsEnabled;
        set { if (_updateControlsEnabled != value) { _updateControlsEnabled = value; OnChanged(); } }
    }

    private double _downloadProgress;
    /// <summary>Download progress, 0–100, driving the progress bar.</summary>
    public double DownloadProgress
    {
        get => _downloadProgress;
        set { if (_downloadProgress != value) { _downloadProgress = value; OnChanged(); OnChanged(nameof(DownloadPercentText)); } }
    }

    /// <summary>Right-aligned percentage label under the progress bar.</summary>
    public string DownloadPercentText => $"{Math.Round(DownloadProgress)}%";

    private bool _isDownloading;
    /// <summary>True only while a download is in progress; shows/hides the whole progress block.</summary>
    public bool IsDownloading
    {
        get => _isDownloading;
        set { if (_isDownloading != value) { _isDownloading = value; OnChanged(); OnChanged(nameof(DownloadVisibility)); } }
    }
    public Visibility DownloadVisibility =>
        IsDownloading ? Visibility.Visible : Visibility.Collapsed;

    private double _focusRefreshFraction = 1;
    /// <summary>Remaining fraction (1→0) of the focused-time refresh cycle. Drives the ring in
    /// the Focused column header.</summary>
    public double FocusRefreshFraction
    {
        get => _focusRefreshFraction;
        private set { if (_focusRefreshFraction != value) { _focusRefreshFraction = value; OnChanged(); } }
    }

    private double _runRefreshFraction = 1;
    /// <summary>Remaining fraction (1→0) of the running-time refresh cycle. Drives the ring in
    /// the Running column header.</summary>
    public double RunRefreshFraction
    {
        get => _runRefreshFraction;
        private set { if (_runRefreshFraction != value) { _runRefreshFraction = value; OnChanged(); } }
    }

    private bool _showFocusWheel;
    public bool ShowFocusWheel
    {
        get => _showFocusWheel;
        private set { if (_showFocusWheel != value) { _showFocusWheel = value; OnChanged(nameof(FocusWheelVisibility)); } }
    }
    public Visibility FocusWheelVisibility =>
        ShowFocusWheel ? Visibility.Visible : Visibility.Collapsed;

    private bool _showRunWheel;
    public bool ShowRunWheel
    {
        get => _showRunWheel;
        private set { if (_showRunWheel != value) { _showRunWheel = value; OnChanged(nameof(RunWheelVisibility)); } }
    }
    public Visibility RunWheelVisibility =>
        ShowRunWheel ? Visibility.Visible : Visibility.Collapsed;

    public DashboardViewModel(UsageRepository usage, UsageTracker tracker, SettingsRepository settings,
        CategoryRepository categories)
    {
        _usage = usage;
        _tracker = tracker;
        _settings = settings;
        _categories = categories;
        _labels = _settings.GetEfficiencySettings();
        RebuildTagChoices();
        _dispatcher = Dispatcher.CurrentDispatcher;
        _wheelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _wheelTimer.Tick += (_, _) => UpdateWheel();
        _tracker.FocusSampled += OnFocusSampled;
        _tracker.RunSampled += OnRunSampled;
        ApplyRefreshSettings();
        _wheelTimer.Start();
        Reload();
    }

    // Sample events arrive on the tracker's timer thread; marshal to the UI thread, restart that
    // metric's ring at full, and reload so the displayed time updates the instant the ring resets.
    private void OnFocusSampled() => _dispatcher.BeginInvoke(() =>
    {
        _focusEpochUtc = DateTime.UtcNow;
        IsUserIdle = _tracker.IsUserIdle;
        // Only rebuild the grid when it's actually on screen; the Page setter reloads on return.
        if (_page == DashboardPage.Dashboard) Reload();
    });

    private void OnRunSampled() => _dispatcher.BeginInvoke(() =>
    {
        _runEpochUtc = DateTime.UtcNow;
        if (_page == DashboardPage.Dashboard) Reload();
    });

    private void UpdateWheel()
    {
        var now = DateTime.UtcNow;
        FocusRefreshFraction = RemainingFraction((now - _focusEpochUtc).TotalSeconds, _focusSeconds);
        RunRefreshFraction = RemainingFraction((now - _runEpochUtc).TotalSeconds, _runSeconds);
    }

    private static double RemainingFraction(double now, double period)
    {
        if (period <= 0) return 0;
        return 1 - (now % period) / period;
    }

    /// <summary>Re-reads the refresh intervals. The grid auto-refreshes at the faster of the two
    /// (pending deltas are merged in, so the slower metric still appears live); each header ring
    /// counts down its own interval. The manual button hides when both are one second or less.</summary>
    public void ApplyRefreshSettings()
    {
        int focus = _settings.GetFocusRefreshSeconds();
        int run = _settings.GetRunRefreshSeconds();
        _focusSeconds = Math.Max(1, focus);
        _runSeconds = Math.Max(1, run);
        ResetCycle();
        ShowFocusWheel = _focusSeconds > 1;
        ShowRunWheel = _runSeconds > 1;
        ShowManualRefresh = Math.Min(_focusSeconds, _runSeconds) > 1;
    }

    private void ResetCycle()
    {
        _focusEpochUtc = _runEpochUtc = DateTime.UtcNow;
        UpdateWheel();
    }

    /// <summary>Force-write pending in-memory deltas, then refresh the grid immediately.</summary>
    public void ForceRefresh()
    {
        ResetCycle();
        _tracker.Flush();
        IsUserIdle = _tracker.IsUserIdle;
        Reload();
    }

    public void Reload()
    {
        // Skip the rebuild while the user is mid drag-select; the view forces a catch-up Reload on
        // mouse-up so the grid still ends on fresh data.
        if (SuspendRefresh) return;

        // DB holds flushed totals; merge the tracker's unflushed in-memory deltas on top so the
        // grid advances every refresh tick instead of only at the 30 s flush. Every range ends at
        // today, so today's pending always belongs in the displayed totals.
        var merged = new Dictionary<string, (string Name, long Run, long Focus, string Category, AppTag Tag)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var r in _usage.Query(SelectedRange))
            merged[r.ExeName] = (r.DisplayName, r.RunMs, r.FocusMs, r.CategoryName, r.Tag);

        foreach (var p in _tracker.SnapshotPending())
        {
            var had = merged.TryGetValue(p.ExeName, out var cur);
            var name = string.IsNullOrEmpty(cur.Name) ? p.DisplayName : cur.Name;
            // A brand-new app isn't in the DB yet (EnsureApp runs at flush time), so apply the same
            // auto-category rule here to show its category immediately instead of "Uncategorized"
            // until the next flush re-reads it.
            var cat = had
                ? (string.IsNullOrEmpty(cur.Category) ? "Uncategorized" : cur.Category)
                : (CategoryRules.Guess(p.ExeName, p.Publisher) ?? "Uncategorized");
            var tag = had ? cur.Tag : AppTag.Other;
            merged[p.ExeName] = (name, cur.Run + p.RunMs, cur.Focus + p.FocusMs, cat, tag);
        }

        // Only apps with time accrued are shown.
        var wanted = merged.Where(kv => kv.Value.Run > 0 || kv.Value.Focus > 0)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        // Per-website focused time for browser rows: flushed totals plus unflushed pending, summed by
        // host and grouped under the owning browser exe, so the expandable breakdown advances live too.
        var sitesByExe = new Dictionary<string, Dictionary<string, long>>(StringComparer.OrdinalIgnoreCase);
        void AddSite(string exe, string host, long focus)
        {
            if (!sitesByExe.TryGetValue(exe, out var hosts))
                sitesByExe[exe] = hosts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            hosts[host] = hosts.TryGetValue(host, out var cur) ? cur + focus : focus;
        }
        foreach (var s in _usage.QuerySites(SelectedRange)) AddSite(s.ExeName, s.Host, s.FocusMs);
        foreach (var s in _tracker.SnapshotPendingSites()) AddSite(s.ExeName, s.Host, s.FocusMs);

        // Update the row collection in place rather than rebuilding it: drop rows that vanished,
        // update the ones that remain, add the new ones. Keeping the same row objects means the
        // grid's selection (and its shift/drag anchor) survives every refresh tick. Display order is
        // owned by the grid's collection view, which re-sorts live as these values change
        // (see MainWindow: SortDescriptions + IsLiveSorting).
        for (int i = Rows.Count - 1; i >= 0; i--)
            if (!wanted.ContainsKey(Rows[i].ExeName))
                Rows.RemoveAt(i);

        var existing = new Dictionary<string, UsageRowVm>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows) existing[row.ExeName] = row;

        long totalRun = 0, totalFocus = 0;
        foreach (var kv in wanted)
        {
            if (!existing.TryGetValue(kv.Key, out var row))
            {
                row = new UsageRowVm(kv.Key, ResolveTagName);
                Rows.Add(row);
            }
            row.DisplayName = kv.Value.Name;
            row.RunMs = kv.Value.Run;
            row.FocusMs = kv.Value.Focus;
            row.CategoryName = kv.Value.Category;
            row.Tag = kv.Value.Tag;
            row.UpdateSites(sitesByExe.TryGetValue(kv.Key, out var hosts)
                ? hosts
                : EmptySites);
            totalRun += kv.Value.Run;
            totalFocus += kv.Value.Focus;
        }
        _totalRunMs = totalRun;
        _totalFocusMs = totalFocus;
        RefreshTotalsDisplay();
        UpdateEfficiency(wanted.Values, totalFocus);
    }

    /// <summary>Called by the dashboard when the grid selection changes. With <see cref="SelectionSummaryMin"/>
    /// or more rows selected, the header totals switch to the combined times of just those rows;
    /// otherwise they return to the range totals.</summary>
    public void UpdateSelectionTotals(IEnumerable<UsageRowVm> selectedRows)
    {
        _selectedRows.Clear();
        _selectedRows.AddRange(selectedRows);
        RefreshTotalsDisplay();
    }

    // Decide whether the header shows the range totals or the current selection's combined times, and
    // format both labels accordingly. Re-run on every refresh so a held selection stays summed with
    // fresh values.
    private void RefreshTotalsDisplay()
    {
        // Drop any selected rows a refresh may have pruned out of the range.
        var selected = _selectedRows.Where(Rows.Contains).ToList();
        if (selected.Count >= SelectionSummaryMin)
        {
            long run = 0, focus = 0;
            foreach (var r in selected) { run += r.RunMs; focus += r.FocusMs; }
            TotalRun = TimeFormat.Humanize(run);
            TotalFocus = TimeFormat.Humanize(focus);
            RunLabel = "Selected Running: ";
            FocusLabel = "Selected Focused: ";
        }
        else
        {
            TotalRun = TimeFormat.Humanize(_totalRunMs);
            TotalFocus = TimeFormat.Humanize(_totalFocusMs);
            RunLabel = "Total Running: ";
            FocusLabel = "Total Focused: ";
        }
    }

    /// <summary>Recompute the efficiency rating from how the range's focused time splits across tags.</summary>
    private void UpdateEfficiency(
        IEnumerable<(string Name, long Run, long Focus, string Category, AppTag Tag)> rows, long totalFocus)
    {
        var rowList = rows.ToList();
        bool hasFocus = totalFocus > 0;
        double score = Efficiency.Score(rowList.Select(r => (r.Tag, r.Focus)), _labels.Weight);
        var rating = _labels.RatingFor(score, hasFocus);

        EfficiencyLevel = _labels.LevelFor(score, hasFocus);
        EfficiencyText = hasFocus && rating is not null
            ? $"Efficiency: {rating.Name} ({score:P0})"
            : "Efficiency: —";

        if (!hasFocus)
        {
            EfficiencyTooltip = "No focused time recorded in this range.";
            return;
        }

        // Per-tag share of focused time, in the fixed tag order, using the user's tag names.
        var byTag = rowList
            .GroupBy(r => r.Tag)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Focus));
        var parts = EfficiencySettings.Tags
            .Select(t => $"{_labels.TagName(t)}: {(byTag.TryGetValue(t, out var ms) ? (double)ms / totalFocus : 0):P0}");
        EfficiencyTooltip = "Share of focused time by tag\n" + string.Join("\n", parts);
    }

    /// <summary>Reload the categories management page from the database (category list, the per-app
    /// dropdown choices, and every known app with its current assignment).</summary>
    public void LoadCategoriesPage()
    {
        // Detach the previous page's rows first: clearing CategoryChoices below pushes a null
        // SelectedValue back through the still-bound ComboBoxes, which would otherwise persist
        // "Uncategorized"/"Other" over the user's real assignments.
        foreach (var a in ManagedApps) a.Detached = true;

        var cats = _categories.List();

        CategoryChoices.Clear();
        CategoryChoices.Add(new CategoryChoice(null, "Uncategorized"));
        foreach (var c in cats) CategoryChoices.Add(new CategoryChoice(c.Id, c.Name));

        ManagedCategories.Clear();
        foreach (var c in cats)
            ManagedCategories.Add(new CategoryItemVm(c.Id, c.Name, c.IsAuto, RenameCategory));

        ManagedApps.Clear();
        foreach (var a in _categories.ListApps())
            ManagedApps.Add(new AppCategoryItemVm(
                a.AppId, a.DisplayName, a.ExeName, a.CategoryId, a.Tag,
                AssignAppCategory, AssignAppTag));
    }

    public void CreateCategory(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        _categories.Create(name.Trim());
        LoadCategoriesPage();
    }

    public void DeleteCategory(long id)
    {
        _categories.Delete(id);
        LoadCategoriesPage();
    }

    private void RenameCategory(long id, string name) => _categories.Rename(id, name);

    private void AssignAppCategory(long appId, long? categoryId) =>
        _categories.AssignApp(appId, categoryId);

    private void AssignAppTag(long appId, AppTag tag) => _categories.AssignTag(appId, tag);

    /// <summary>Delete a watched app and its usage. It reappears the next time it's detected.</summary>
    public void DeleteApp(string exeName) => DeleteApps(new[] { exeName });

    /// <summary>Block an app: delete it and stop detecting it until the user unblocks it.</summary>
    public void BlockApp(string exeName) => BlockApps(new[] { exeName });

    /// <summary>Delete several apps at once, reloading the grid only after the whole batch.</summary>
    public void DeleteApps(IEnumerable<string> exeNames)
    {
        foreach (var exe in exeNames)
        {
            _tracker.ForgetPending(exe);
            _usage.DeleteApp(exe);
        }
        Reload();
    }

    /// <summary>Block several apps at once, reloading the grid only after the whole batch.</summary>
    public void BlockApps(IEnumerable<string> exeNames)
    {
        foreach (var exe in exeNames)
        {
            _tracker.Block(exe);
            _usage.DeleteApp(exe);
        }
        Reload();
    }

    /// <summary>Unblock a previously blocked app so it's detected again.</summary>
    public void UnblockApp(string exeName)
    {
        _tracker.Unblock(exeName);
        LoadBlocked();
    }

    /// <summary>Every currently-selected website across all browser rows.</summary>
    public IEnumerable<SiteRowVm> SelectedSites() =>
        Rows.SelectMany(r => r.Sites).Where(s => s.IsSelected);

    /// <summary>Clear any website selection across every browser row.</summary>
    public void ClearSiteSelection()
    {
        foreach (var s in Rows.SelectMany(r => r.Sites))
            s.IsSelected = false;
    }

    /// <summary>Delete a mixed selection of apps and websites in one batch. Sites whose browser is
    /// itself being deleted are skipped (the app delete already removes all its site rows). The grid
    /// reloads once after the whole batch.</summary>
    public void DeleteSelection(IReadOnlyCollection<string> appExes, IReadOnlyCollection<SiteRowVm> sites)
    {
        // Flush pending deltas first so the DB holds authoritative totals: a site's focus is accrued to
        // both the browser and the site, so DeleteSite subtracts it from the browser's daily focus — and
        // that only works once both have landed in the DB (otherwise the still-pending browser focus
        // would keep the removed time).
        _tracker.Flush();
        var appSet = new HashSet<string>(appExes, StringComparer.OrdinalIgnoreCase);
        foreach (var exe in appExes)
        {
            _tracker.ForgetPending(exe);
            _usage.DeleteApp(exe);
        }
        foreach (var s in sites)
        {
            if (appSet.Contains(s.ExeName)) continue;
            _tracker.ForgetPendingSite(s.ExeName, s.Host);
            _usage.DeleteSite(s.ExeName, s.Host);
        }
        Reload();
    }

    /// <summary>Block a mixed selection of apps and websites in one batch. Apps stop being detected;
    /// websites are blocked globally by host. Sites whose browser is itself being blocked are skipped
    /// (the app delete already removes all its site rows). The grid reloads once after the batch.</summary>
    public void BlockSelection(IReadOnlyCollection<string> appExes, IReadOnlyCollection<SiteRowVm> sites)
    {
        // Flush first for the same reason as DeleteSelection: DeleteSiteByHost subtracts each browser's
        // recorded focus for the host from its daily total, which needs both sides flushed to the DB.
        _tracker.Flush();
        var appSet = new HashSet<string>(appExes, StringComparer.OrdinalIgnoreCase);
        foreach (var exe in appExes)
        {
            _tracker.Block(exe);
            _usage.DeleteApp(exe);
        }
        foreach (var s in sites)
        {
            if (appSet.Contains(s.ExeName)) continue;
            _tracker.BlockSite(s.Host);
            _usage.DeleteSiteByHost(s.Host);
        }
        Reload();
    }

    /// <summary>Unblock a previously blocked website so its per-site time is tracked again.</summary>
    public void UnblockSite(string host)
    {
        _tracker.UnblockSite(host);
        LoadBlocked();
    }

    /// <summary>Load the blocked apps and websites from the tracker for the Settings page.</summary>
    public void LoadBlocked()
    {
        BlockedApps.Clear();
        foreach (var exe in _tracker.BlockedList())
            BlockedApps.Add(exe);
        OnChanged(nameof(NoBlockedAppsVisibility));

        BlockedSites.Clear();
        foreach (var host in _tracker.BlockedSitesList())
            BlockedSites.Add(host);
        OnChanged(nameof(NoBlockedSitesVisibility));
    }

    // Resolves a tag's display name from the current label settings; passed to each row so the
    // dashboard Tag column reflects the user's names and updates when they're changed.
    private string ResolveTagName(AppTag tag) => _labels.TagName(tag);

    // Rebuild the per-app tag dropdown choices from the current labels. Detach the categories page's
    // app rows first: clearing TagChoices pushes a null SelectedValue back through any still-bound
    // ComboBox, which the Detached guard ignores. They're recreated fresh on the next page load.
    private void RebuildTagChoices()
    {
        foreach (var a in ManagedApps) a.Detached = true;
        TagChoices.Clear();
        foreach (var t in EfficiencySettings.Tags)
            TagChoices.Add(new TagChoice(t, _labels.TagName(t)));
    }

    /// <summary>Populate the settings-page tag/efficiency editors from the saved labels and weights.</summary>
    public void LoadEfficiencyLabelsForEditing()
    {
        TagLabels.Clear();
        foreach (var t in EfficiencySettings.Tags)
            TagLabels.Add(new TagLabelVm(t, _labels.TagName(t), _labels.Weight(t)));

        RatingLabels.Clear();
        foreach (var r in _labels.Ratings)
            RatingLabels.Add(new RatingLabelVm(r.Name, r.Threshold));
    }

    /// <summary>Append a new editable rating, defaulting its threshold just above the current highest
    /// so it slots in at the top. Saved only when the user clicks Save.</summary>
    public void AddRating()
    {
        double maxPct = 0;
        foreach (var row in RatingLabels)
            if (double.TryParse(row.ThresholdPercent, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                maxPct = Math.Max(maxPct, p);
        var next = Math.Min(95, maxPct + 10);
        RatingLabels.Add(new RatingLabelVm("New rating", next / 100.0));
    }

    /// <summary>Remove an editable rating, keeping at least two so the scale stays meaningful.</summary>
    public bool RemoveRating(RatingLabelVm row, out string? error)
    {
        error = null;
        if (RatingLabels.Count <= 2)
        {
            error = "Keep at least two efficiency ratings.";
            return false;
        }
        RatingLabels.Remove(row);
        return true;
    }

    /// <summary>Validate and persist the edited tag/efficiency labels and weights. On success the new
    /// settings are applied live (dropdown choices, grid tag names, and the efficiency pill all
    /// refresh). Returns false with a message when a name is blank or a weight isn't a number â‰¥ 0.</summary>
    public bool TrySaveEfficiencyLabels(out string? error)
    {
        error = null;

        var names = new Dictionary<AppTag, string>();
        var weights = new Dictionary<AppTag, double>();
        foreach (var row in TagLabels)
        {
            var name = row.Name?.Trim();
            if (string.IsNullOrEmpty(name)) { error = "Tag names can't be blank."; return false; }
            if (!double.TryParse(row.Weight, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) || w < 0)
            {
                error = "Tag weights must be numbers of 0 or more.";
                return false;
            }
            names[row.Tag] = name;
            weights[row.Tag] = w;
        }

        if (RatingLabels.Count < 2) { error = "Add at least two efficiency ratings."; return false; }

        var ratings = new List<EfficiencyRating>();
        foreach (var row in RatingLabels)
        {
            var name = row.Name?.Trim();
            if (string.IsNullOrEmpty(name)) { error = "Rating names can't be blank."; return false; }
            if (!double.TryParse(row.ThresholdPercent, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)
                || pct < 0 || pct > 100)
            {
                error = "Rating thresholds must be percentages between 0 and 100.";
                return false;
            }
            ratings.Add(new EfficiencyRating(name, pct / 100.0));
        }

        // Sort by threshold and require distinct values so each rating is reachable; the lowest rating
        // becomes the floor (0%) regardless of what was typed.
        ratings.Sort((a, b) => a.Threshold.CompareTo(b.Threshold));
        for (int i = 1; i < ratings.Count; i++)
            if (ratings[i].Threshold <= ratings[i - 1].Threshold)
            {
                error = "Each rating needs a higher threshold than the one below it.";
                return false;
            }
        ratings[0] = ratings[0] with { Threshold = 0.0 };

        _settings.SaveEfficiencySettings(names, weights, ratings);
        _labels = _settings.GetEfficiencySettings();
        RebuildTagChoices();
        foreach (var row in Rows) row.RefreshTagName();
        Reload(); // recompute the pill with the new weights and tier names
        return true;
    }

    public void Dispose()
    {
        _tracker.FocusSampled -= OnFocusSampled;
        _tracker.RunSampled -= OnRunSampled;
        _wheelTimer.Stop();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class TimeFormat
{
    public static string Humanize(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }
}
