using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using ProgramClock.Data;
using ProgramClock.Startup;
using ProgramClock.Tracking;
using ProgramClock.Tray;
using ProgramClock.UI;
using ProgramClock.UI.Theme;
using ProgramClock.Update;
using Application = System.Windows.Application;

namespace ProgramClock;

public partial class App : Application
{
    private const string FirstRunDoneKey = "first_run_done";
    private const string CategoriesBackfilledKey = "categories_backfilled";
    private const string SelfBlockedKey = "self_blocked";

    private Mutex? _singleInstance;
    private Database? _db;
    private UsageTracker? _tracker;
    private TrayMenu? _tray;
    private System.Drawing.Icon? _trayIcon;
    private MainWindow? _dashboard;
    private ThemeManager? _theme;

    public UsageRepository Usage { get; private set; } = null!;
    public SettingsRepository Settings { get; private set; } = null!;
    public CategoryRepository Categories { get; private set; } = null!;
    public BlocklistRepository Blocklist { get; private set; } = null!;
    public UsageTracker Tracker => _tracker!;

    public new static App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, "ProgramClock.SingleInstance", out var isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        _theme = new ThemeManager();
        _theme.Initialize();

        _db = new Database();
        Usage = new UsageRepository(_db.Connection);
        Settings = new SettingsRepository(_db.Connection);
        Categories = new CategoryRepository(_db.Connection);
        Blocklist = new BlocklistRepository(_db.Connection);

        ApplyAccent(Settings.GetAccentColor());
        ApplyMainColor(Settings.GetMainColor());

        if (Settings.Get(FirstRunDoneKey) is null)
        {
            AutoStart.SetEnabled(true);
            Settings.Set(FirstRunDoneKey, "1");
        }

        if (Settings.Get(CategoriesBackfilledKey) is null)
        {
            Categories.BackfillUncategorized();
            Settings.Set(CategoriesBackfilledKey, "1");
        }

        // ProgramClock should never track itself: seed its own exe into the blocklist once. The user
        // can still unblock it later if they want; this is just the out-of-the-box default.
        if (Settings.Get(SelfBlockedKey) is null)
        {
            Blocklist.Block("ProgramClock.exe");
            Settings.Set(SelfBlockedKey, "1");
        }

        _tracker = new UsageTracker(
            Usage,
            Blocklist,
            Settings.GetIdleThresholdSeconds(),
            Settings.GetFocusRefreshSeconds(),
            Settings.GetRunRefreshSeconds());
        _tracker.SetDayHours(Settings.GetDayStart(), Settings.GetDayEnd());
        _tracker.SetLockStopEnabled(Settings.GetLockStopEnabled());
        _tracker.SetLockStopMinutes(Settings.GetLockStopMinutes());
        _tracker.Start();

        _trayIcon = LoadAppIcon();
        _tray = new TrayMenu(_tracker, _trayIcon, ShowDashboard, ShowSettings, QuitApp);

        SystemEvents.SessionEnding += (_, _) => _tracker?.Flush();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        ShowDashboard();

        // Opt-in: only reaches the network if the user enabled the daily check.
        MaybeAutoCheckForUpdates();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        // Fires on wake-from-sleep; run the (opt-in, once-per-day) update check then.
        if (e.Mode == PowerModes.Resume)
            MaybeAutoCheckForUpdates();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        // Feed lock/unlock into the tracker so it can stop running time after the PC has been locked
        // long enough outside the user's day window, and resume it the moment they unlock.
        if (e.Reason == SessionSwitchReason.SessionLock)
            _tracker?.SetSessionLocked(true);
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
            _tracker?.SetSessionLocked(false);
    }

    /// <summary>Runs the automatic update check at most once a day, and only if the user opted in.</summary>
    private void MaybeAutoCheckForUpdates()
    {
        if (!Settings.GetAutoUpdateEnabled()) return;

        var today = UsageRepository.LocalDate(DateTime.Now);
        if (Settings.GetLastUpdateCheckDate() == today) return;
        Settings.SetLastUpdateCheckDate(today);

        _ = AutoCheckAsync();
    }

    private async Task AutoCheckAsync()
    {
        var info = await UpdateService.CheckAsync().ConfigureAwait(false);
        if (info is not null && UpdateService.IsUpdateAvailable(info))
            Dispatcher.Invoke(() => _tray?.ShowUpdateAvailable(info.Tag));
    }

    /// <summary>Installs a downloaded update and restarts the app (called from the Settings page).</summary>
    public void ApplyUpdateAndRestart(string newExePath)
    {
        UpdateService.ApplyAndRestart(newExePath);
        QuitApp();
    }

    private void ShowDashboard()
    {
        // Keep one dashboard instance alive for the app's lifetime: a user close just hides it (see
        // MainWindow.OnWindowClosing), so its view model — and the per-browser website breakdown it
        // holds — survives across open/close instead of being rebuilt each time.
        _dashboard ??= new MainWindow();
        if (!_dashboard.IsVisible)
            _dashboard.Show();
        if (_dashboard.WindowState == WindowState.Minimized)
            _dashboard.WindowState = WindowState.Normal;
        _dashboard.Activate();
    }

    private void ShowSettings()
    {
        ShowDashboard();
        _dashboard?.NavigateToSettings();
    }

    public void NotifyAutoStartChanged() => _tray?.RefreshAutoStart();

    /// <summary>Applies the accent colour live everywhere. The default red falls back to the theme's
    /// built-in per-mode shade; any other colour overrides it in both light and dark.</summary>
    public void ApplyAccent(string hex) =>
        _theme?.ApplyAccent(
            string.Equals(hex, SettingsRepository.DefaultAccentColor, StringComparison.OrdinalIgnoreCase)
                ? null
                : hex);

    /// <summary>Applies the main (background) colour live. An empty value follows the Windows
    /// light/dark theme; any colour overrides the background and derives a matching palette.</summary>
    public void ApplyMainColor(string hex) =>
        _theme?.ApplyMainColor(string.IsNullOrWhiteSpace(hex) ? null : hex);

    private static System.Drawing.Icon LoadAppIcon()
    {
        var info = GetResourceStream(new Uri("app.ico", UriKind.Relative));
        if (info is not null)
        {
            using var stream = info.Stream;
            return new System.Drawing.Icon(stream);
        }
        return System.Drawing.SystemIcons.Application;
    }

    private void QuitApp()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        if (_dashboard is not null)
        {
            _dashboard.AllowClose = true;   // let it really close (and dispose its view model)
            _dashboard.Close();
            _dashboard = null;
        }
        _tracker?.Dispose();
        _tray?.Dispose();
        _trayIcon?.Dispose();
        _theme?.Dispose();
        _db?.Dispose();
        _singleInstance?.ReleaseMutex();
        Shutdown();
    }
}
