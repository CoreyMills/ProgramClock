using System.Drawing;
using System.Windows.Forms;
using ProgramClock.Startup;
using ProgramClock.Tracking;

namespace ProgramClock.Tray;

/// <summary>System-tray icon and context menu. Owns no business logic beyond wiring callbacks.</summary>
public sealed class TrayMenu : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly UsageTracker _tracker;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _autoStartItem;

    public TrayMenu(UsageTracker tracker, Icon icon, Action openDashboard, Action openSettings, Action quit)
    {
        _tracker = tracker;

        var menu = new ContextMenuStrip();

        var open = new ToolStripMenuItem("Open Dashboard", null, (_, _) => openDashboard());
        open.Font = new Font(open.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add(open);
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => openSettings()));
        menu.Items.Add(new ToolStripSeparator());

        _pauseItem = new ToolStripMenuItem("Pause tracking", null, (_, _) => TogglePause());
        menu.Items.Add(_pauseItem);

        _autoStartItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutoStart())
        {
            Checked = AutoStart.IsEnabled(),
            CheckOnClick = false,
        };
        menu.Items.Add(_autoStartItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => quit()));

        _icon = new NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "ProgramClock",
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => openDashboard();
    }

    public void RefreshAutoStart() => _autoStartItem.Checked = AutoStart.IsEnabled();

    /// <summary>Shows a tray balloon when the daily auto-check finds a newer release.</summary>
    public void ShowUpdateAvailable(string tag) =>
        _icon.ShowBalloonTip(8000, "ProgramClock update available",
            $"Version {tag} is available. Open Settings to update.", ToolTipIcon.Info);

    private void TogglePause()
    {
        if (_tracker.IsPaused) _tracker.Resume(); else _tracker.Pause();
        _pauseItem.Text = _tracker.IsPaused ? "Resume tracking" : "Pause tracking";
    }

    private void ToggleAutoStart()
    {
        var next = !AutoStart.IsEnabled();
        AutoStart.SetEnabled(next);
        _autoStartItem.Checked = next;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
