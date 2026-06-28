using System.Globalization;
using System.Timers;
using ProgramClock.Data;
using Timer = System.Timers.Timer;

namespace ProgramClock.Tracking;

/// <summary>
/// The always-on tracking engine. A 1 s tick accrues focused time to the foreground app
/// (unless the user is idle); a 5 s tick accrues run time to every visible app; a 30 s tick
/// flushes accumulated deltas to SQLite. Time is counted in fixed per-tick increments, so a
/// sleep/hibernate gap (which suspends the timers) is simply not counted — no baseline to reset.
/// </summary>
public sealed class UsageTracker : IDisposable
{
    private const int FlushTickMs = 30_000;

    private readonly UsageRepository _usage;
    private readonly BlocklistRepository _blocklist;
    private readonly Timer _focusTimer;
    private readonly Timer _runTimer;
    private readonly Timer _flushTimer = new(FlushTickMs);

    private readonly object _gate = new();
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SitePending> _pendingSites = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blockedSites = new(StringComparer.OrdinalIgnoreCase);

    private volatile int _idleThresholdMs;
    private volatile int _focusTickMs;
    private volatile int _runTickMs;
    private volatile bool _paused;
    private volatile bool _isUserIdle;
    private long _idleMs;

    // ── Run-time accrual gates (see ShouldAccrueRun) ────────────────────────────────────────────────
    // 1. Day gate: running time for a new local day doesn't start until the user has had at least one
    //    focused increment that day — i.e. they have actually started using the PC. Resets at midnight.
    private string? _gateDate;   // local date (yyyy-MM-dd) the gate fields below apply to
    private bool _dayStarted;    // has a focused increment landed today? (guarded by _gate)

    // 2. Outside-hours lock: when enabled, running time stops once the session has been locked for at
    //    least _lockStopMs AND the current local time is outside the [_dayStartMin, _dayEndMin] window.
    //    Unlocking clears the lock immediately, so resuming work outside normal hours starts it again.
    private volatile bool _sessionLocked;
    private long _lockedSinceTicks;              // DateTime.UtcNow.Ticks when the lock began; 0 if unlocked
    private volatile int _dayStartMin = 8 * 60;  // day window, minutes since local midnight
    private volatile int _dayEndMin = 22 * 60;
    private volatile int _lockStopMs = 15 * 60 * 1000;
    private volatile bool _lockStopEnabled = true;

    private sealed class Pending
    {
        public AppInfo Info = null!;
        public long RunMs;
        public long FocusMs;
    }

    // Focused time for one website under a browser. Keyed in _pendingSites by "exe|host"; Browser
    // carries the owning browser's metadata so the flush can EnsureApp it.
    private sealed class SitePending
    {
        public AppInfo Browser = null!;
        public string Host = "";
        public long FocusMs;
    }

    public UsageTracker(UsageRepository usage, BlocklistRepository blocklist, int idleThresholdSeconds,
        int focusRefreshSeconds, int runRefreshSeconds)
    {
        _usage = usage;
        _blocklist = blocklist;
        foreach (var exe in blocklist.List())
            _blocked.Add(exe);
        foreach (var host in blocklist.ListSites())
            _blockedSites.Add(host);
        _idleThresholdMs = idleThresholdSeconds * 1000;
        _focusTickMs = Math.Max(1, focusRefreshSeconds) * 1000;
        _runTickMs = Math.Max(1, runRefreshSeconds) * 1000;

        _focusTimer = new Timer(_focusTickMs);
        _runTimer = new Timer(_runTickMs);

        _focusTimer.Elapsed += OnFocusTick;
        _runTimer.Elapsed += OnRunTick;
        _flushTimer.Elapsed += (_, _) => Flush();
        _focusTimer.AutoReset = _runTimer.AutoReset = _flushTimer.AutoReset = true;
    }

    /// <summary>Raised on the timer thread at the end of each focus/run sampling tick. The dashboard
    /// uses these to reset its countdown ring and reload the grid exactly when new data lands, so the
    /// displayed times advance in lock-step with the ring instead of lagging behind it.</summary>
    public event Action? FocusSampled;
    public event Action? RunSampled;

    public bool IsPaused => _paused;

    /// <summary>True when the last focus tick saw idle time at or past the threshold.</summary>
    public bool IsUserIdle => _isUserIdle;

    /// <summary>Idle milliseconds observed at the last focus tick.</summary>
    public long IdleMs => Interlocked.Read(ref _idleMs);

    public void SetIdleThresholdSeconds(int seconds) =>
        _idleThresholdMs = Math.Max(1, seconds) * 1000;

    public void SetFocusIntervalSeconds(int seconds)
    {
        _focusTickMs = Math.Max(1, seconds) * 1000;
        _focusTimer.Interval = _focusTickMs;
    }

    public void SetRunIntervalSeconds(int seconds)
    {
        _runTickMs = Math.Max(1, seconds) * 1000;
        _runTimer.Interval = _runTickMs;
    }

    /// <summary>Sets the user's day window from "HH:mm" strings; unparseable values fall back to 08:00/22:00.</summary>
    public void SetDayHours(string startHHmm, string endHHmm)
    {
        _dayStartMin = ParseMinutes(startHHmm, 8 * 60);
        _dayEndMin = ParseMinutes(endHHmm, 22 * 60);
    }

    public void SetLockStopMinutes(int minutes) => _lockStopMs = Math.Max(0, minutes) * 60_000;

    public void SetLockStopEnabled(bool enabled) => _lockStopEnabled = enabled;

    /// <summary>Called from the OS session lock/unlock events so outside-hours suppression can measure
    /// how long the PC has been locked. Locking starts the clock; unlocking clears it at once.</summary>
    public void SetSessionLocked(bool locked)
    {
        if (locked)
        {
            // Keep the original lock instant if multiple lock events arrive without an unlock.
            if (!_sessionLocked)
            {
                Interlocked.Exchange(ref _lockedSinceTicks, DateTime.UtcNow.Ticks);
                _sessionLocked = true;
            }
        }
        else
        {
            _sessionLocked = false;
            Interlocked.Exchange(ref _lockedSinceTicks, 0);
        }
    }

    public void Start()
    {
        _focusTimer.Start();
        _runTimer.Start();
        _flushTimer.Start();
    }

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    private void OnFocusTick(object? sender, ElapsedEventArgs e)
    {
        if (_paused) return;
        var snap = ForegroundProbe.Capture();
        Interlocked.Exchange(ref _idleMs, snap.IdleMs);
        // Treat a locked session as idle: no focused time (and no "day started") should accrue at the
        // lock screen, even while the password prompt briefly registers input.
        var idle = _sessionLocked || snap.IdleMs >= _idleThresholdMs;
        _isUserIdle = idle;
        if (snap.App is not null && !idle)
        {
            Accrue(snap.App, runMs: 0, focusMs: _focusTickMs);
            MarkDayStarted();   // the first focused increment opens the run-time gate for today
            // Browsers also accrue the same focused tick to the active tab's website, so the per-site
            // breakdown sums to (at most) the browser's own focused time.
            if (snap.Host is not null)
                AccrueSite(snap.App, snap.Host, _focusTickMs);
        }
        FocusSampled?.Invoke();
    }

    private void OnRunTick(object? sender, ElapsedEventArgs e)
    {
        if (_paused) return;
        if (ShouldAccrueRun(DateTime.Now))
        {
            foreach (var info in ProcessProbe.CaptureUserFacing())
                Accrue(info, runMs: _runTickMs, focusMs: 0);
        }
        RunSampled?.Invoke();
    }

    // ── Run-time gating ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Marks that the user has started using the PC today, opening the run-time gate.</summary>
    private void MarkDayStarted()
    {
        var today = UsageRepository.LocalDate(DateTime.Now);
        lock (_gate)
        {
            RollDayGateIfNeeded(today);
            _dayStarted = true;
        }
    }

    /// <summary>Resets the day gate when the local date advances. Must be called holding <see cref="_gate"/>.</summary>
    private void RollDayGateIfNeeded(string today)
    {
        if (_gateDate != today)
        {
            _gateDate = today;
            _dayStarted = false;
        }
    }

    /// <summary>Whether running time should accrue right now: only after the user has started using the
    /// PC today, and not while suppressed by the outside-hours lock rule.</summary>
    private bool ShouldAccrueRun(DateTime now)
    {
        bool started;
        lock (_gate)
        {
            RollDayGateIfNeeded(UsageRepository.LocalDate(now));
            started = _dayStarted;
        }
        if (!started) return false;
        return !IsSuppressedByOutsideHoursLock(now);
    }

    private bool IsSuppressedByOutsideHoursLock(DateTime now)
    {
        if (!_lockStopEnabled || !_sessionLocked) return false;
        var lockedSince = Interlocked.Read(ref _lockedSinceTicks);
        if (lockedSince == 0) return false;
        var lockedForMs = (DateTime.UtcNow.Ticks - lockedSince) / TimeSpan.TicksPerMillisecond;
        if (lockedForMs < _lockStopMs) return false;
        return !IsWithinDayHours(now);
    }

    private bool IsWithinDayHours(DateTime now)
    {
        int start = _dayStartMin, end = _dayEndMin;
        if (start == end) return true;   // empty window => never suppress
        int cur = now.Hour * 60 + now.Minute;
        return start < end
            ? cur >= start && cur < end
            : cur >= start || cur < end; // window wraps past midnight
    }

    // Parses "HH:mm" into minutes since midnight, or returns the fallback if it can't be parsed.
    private static int ParseMinutes(string hhmm, int fallback) =>
        TimeOnly.TryParse(hhmm, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t.Hour * 60 + t.Minute
            : fallback;

    /// <summary>Unflushed in-memory deltas (ms) accrued today, keyed by exe. Lets the dashboard
    /// show live totals between flushes so the displayed times advance at the refresh cadence.</summary>
    public readonly record struct PendingSnapshot(
        string ExeName, string DisplayName, string? Publisher, long RunMs, long FocusMs);

    public List<PendingSnapshot> SnapshotPending()
    {
        lock (_gate)
        {
            return _pending.Values
                .Select(p => new PendingSnapshot(
                    p.Info.ExeName, p.Info.DisplayName ?? p.Info.ExeName, p.Info.Publisher, p.RunMs, p.FocusMs))
                .ToList();
        }
    }

    /// <summary>Unflushed per-website focused deltas (ms), keyed by browser exe + host.</summary>
    public readonly record struct SitePendingSnapshot(string ExeName, string Host, long FocusMs);

    public List<SitePendingSnapshot> SnapshotPendingSites()
    {
        lock (_gate)
        {
            return _pendingSites.Values
                .Select(s => new SitePendingSnapshot(s.Browser.ExeName, s.Host, s.FocusMs))
                .ToList();
        }
    }

    private void Accrue(AppInfo info, long runMs, long focusMs)
    {
        lock (_gate)
        {
            if (_blocked.Contains(info.ExeName)) return;
            if (!_pending.TryGetValue(info.ExeName, out var p))
                _pending[info.ExeName] = p = new Pending { Info = info };
            p.Info = info;
            p.RunMs += runMs;
            p.FocusMs += focusMs;
        }
    }

    private void AccrueSite(AppInfo browser, string host, long focusMs)
    {
        lock (_gate)
        {
            if (_blocked.Contains(browser.ExeName)) return;
            if (_blockedSites.Contains(host)) return;
            var key = browser.ExeName + "|" + host;
            if (!_pendingSites.TryGetValue(key, out var s))
                _pendingSites[key] = s = new SitePending { Browser = browser, Host = host };
            s.Browser = browser;
            s.FocusMs += focusMs;
        }
    }

    // Drop every pending site row belonging to an exe (used when it's blocked or forgotten).
    private void RemovePendingSites(string exeName)
    {
        var prefix = exeName + "|";
        foreach (var key in _pendingSites.Keys
                     .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            _pendingSites.Remove(key);
    }

    /// <summary>Block an exe: persist it, add it to the in-memory set so it stops accruing, and drop
    /// any unflushed deltas already collected for it.</summary>
    public void Block(string exeName)
    {
        _blocklist.Block(exeName);
        lock (_gate)
        {
            _blocked.Add(exeName);
            _pending.Remove(exeName);
            RemovePendingSites(exeName);
        }
    }

    /// <summary>Unblock an exe so it is detected again on the next tick.</summary>
    public void Unblock(string exeName)
    {
        _blocklist.Unblock(exeName);
        lock (_gate)
            _blocked.Remove(exeName);
    }

    /// <summary>Block a website (global by host): persist it, add it to the in-memory set so it stops
    /// accruing per-site time in every browser, and drop any unflushed deltas already collected for
    /// it. The browser's own run/focus totals are unaffected.</summary>
    public void BlockSite(string host)
    {
        _blocklist.BlockSite(host);
        lock (_gate)
        {
            _blockedSites.Add(host);
            RemovePendingSitesByHost(host);
        }
    }

    /// <summary>Unblock a website so its per-site time is tracked again on the next tick.</summary>
    public void UnblockSite(string host)
    {
        _blocklist.UnblockSite(host);
        lock (_gate)
            _blockedSites.Remove(host);
    }

    public List<string> BlockedSitesList() => _blocklist.ListSites();

    /// <summary>Drop unflushed deltas for one website under one browser without blocking it (used by
    /// Delete so a just-deleted site isn't re-created by the next flush).</summary>
    public void ForgetPendingSite(string exeName, string host)
    {
        lock (_gate)
            _pendingSites.Remove(exeName + "|" + host);
    }

    // Drop every pending site row for a host across all browsers (used when blocking it globally).
    private void RemovePendingSitesByHost(string host)
    {
        var suffix = "|" + host;
        foreach (var key in _pendingSites.Keys
                     .Where(k => k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).ToList())
            _pendingSites.Remove(key);
    }

    /// <summary>Drop any unflushed deltas for an exe without blocking it (used by Delete so a just-
    /// deleted app doesn't get re-created by the next flush).</summary>
    public void ForgetPending(string exeName)
    {
        lock (_gate)
        {
            _pending.Remove(exeName);
            RemovePendingSites(exeName);
        }
    }

    public List<string> BlockedList() => _blocklist.List();

    public void Flush()
    {
        List<Pending> batch;
        List<SitePending> siteBatch;
        lock (_gate)
        {
            if (_pending.Count == 0 && _pendingSites.Count == 0) return;
            batch = _pending.Values.Select(p => new Pending
            {
                Info = p.Info,
                RunMs = p.RunMs,
                FocusMs = p.FocusMs,
            }).ToList();
            siteBatch = _pendingSites.Values.Select(s => new SitePending
            {
                Browser = s.Browser,
                Host = s.Host,
                FocusMs = s.FocusMs,
            }).ToList();
            _pending.Clear();
            _pendingSites.Clear();
        }

        var date = UsageRepository.LocalDate(DateTime.Now);
        foreach (var p in batch)
        {
            var appId = _usage.EnsureApp(p.Info.ExeName, p.Info.DisplayName, p.Info.ExePath, p.Info.Publisher);
            _usage.AddUsage(appId, date, p.RunMs, p.FocusMs);
        }
        foreach (var s in siteBatch)
        {
            var appId = _usage.EnsureApp(
                s.Browser.ExeName, s.Browser.DisplayName, s.Browser.ExePath, s.Browser.Publisher);
            _usage.AddSiteUsage(appId, s.Host, date, s.FocusMs);
        }
    }

    public void Dispose()
    {
        _focusTimer.Stop();
        _runTimer.Stop();
        _flushTimer.Stop();
        Flush();
        _focusTimer.Dispose();
        _runTimer.Dispose();
        _flushTimer.Dispose();
    }
}
