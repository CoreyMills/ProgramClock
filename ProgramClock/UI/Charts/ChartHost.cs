using System.ComponentModel;
using System.Windows.Controls;
using ProgramClock.UI;
// WinForms implicit usings make these ambiguous; pin to the WPF types.
using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;
using Size = System.Windows.Size;
using Point = System.Windows.Point;
using FrameworkElement = System.Windows.FrameworkElement;
using DependencyObject = System.Windows.DependencyObject;
using ToolTip = System.Windows.Controls.ToolTip;
using PlacementMode = System.Windows.Controls.Primitives.PlacementMode;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using VisualTreeHelper = System.Windows.Media.VisualTreeHelper;
using DispatcherTimer = System.Windows.Threading.DispatcherTimer;
using DependencyPropertyChangedEventArgs = System.Windows.DependencyPropertyChangedEventArgs;

namespace ProgramClock.UI.Charts;

/// <summary>Base for the hand-drawn dashboard charts. Hosts a <see cref="Canvas"/> and redraws on
/// resize, on becoming visible, and whenever the view model's <c>ChartData</c> changes. Crucially it
/// only draws while visible, so a collapsed (non-selected) chart does no rendering work — and the view
/// model only shapes data for the selected view, so inactive visualizers stay fully idle.
/// <para>
/// Section tooltips are driven manually (not via <c>ToolTipService</c>): on mouse-move we hit-test the
/// section under the pointer and update a single shared tooltip's content directly. The framework's
/// per-element tooltips lag or fail to switch between many small adjacent shapes (e.g. the donut's
/// wedges); driving one tooltip ourselves makes it update the instant the pointer crosses into a new
/// section and hide as soon as it leaves one.
/// </para></summary>
public abstract class ChartHost : UserControl
{
    protected readonly Canvas Root = new() { ClipToBounds = true };
    private DashboardViewModel? _vm;

    // Shared, manually-driven section tooltip. StaysOpen so only our code opens/closes it.
    private readonly ToolTip _tip = new() { StaysOpen = true, Placement = PlacementMode.Relative };
    private readonly DispatcherTimer _showTimer = new();
    private string? _shownText;

    protected ChartHost()
    {
        Content = Root;
        _tip.PlacementTarget = this;
        _showTimer.Tick += (_, _) => { _showTimer.Stop(); if (_shownText is not null) _tip.IsOpen = true; };

        SizeChanged += (_, _) => Redraw();
        IsVisibleChanged += (_, _) => { if (!IsVisible) HideTip(); Redraw(); };
        DataContextChanged += OnDataContextChanged;
        MouseMove += OnChartMouseMove;
        // On leaving the chart: hide the tooltip and run any rebuild deferred while hovering.
        MouseLeave += (_, _) => { HideTip(); if (_pendingRedraw) Redraw(); };
    }

    // Set when a refresh-tick rebuild is skipped because the pointer is hovering (which would tear down
    // the hovered shape); the rebuild then runs on MouseLeave.
    private bool _pendingRedraw;

    protected ChartSnapshot? Data => _vm?.ChartData;

    /// <summary>The metric the charts currently show ("focused"/"running"), for tooltip text.</summary>
    protected string MetricName => (_vm?.ShowFocused ?? true) ? "focused" : "running";

    /// <summary>Whether the hovered chart section should grow/pop (user setting).</summary>
    protected bool GrowOnHover => _vm?.GrowOnHover ?? true;

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = e.NewValue as DashboardViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
        Redraw();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardViewModel.ChartData)) Redraw();
    }

    private void Redraw()
    {
        // Don't tear down the shapes on a data/refresh tick while the pointer is over the chart — that
        // would drop the hovered section. Defer; MouseLeave runs the rebuild once the pointer exits.
        // (Hover-driven re-renders call RenderNow directly and are not subject to this.)
        if (IsMouseOver && Root.Children.Count > 0 &&
            IsVisible && ActualWidth > 0 && ActualHeight > 0 && Data is not null)
        {
            _pendingRedraw = true;
            return;
        }
        RenderNow();
    }

    /// <summary>Clear and redraw immediately from the current snapshot. Subclasses call this to apply
    /// hover effects even while the pointer is over the chart.</summary>
    protected void RenderNow()
    {
        double w = ActualWidth, h = ActualHeight;
        var data = Data;
        if (!IsVisible || w <= 0 || h <= 0 || data is null)
        {
            // collapsed/non-selected/Table or no snapshot: nothing to show
            Root.Children.Clear();
            _pendingRedraw = false;
            return;
        }

        Root.Children.Clear();
        if (IsEmpty(data)) DrawCenteredMessage("No data for this range.");
        else Draw(data, w, h);
        _pendingRedraw = false;
    }

    protected abstract bool IsEmpty(ChartSnapshot data);
    protected abstract void Draw(ChartSnapshot data, double width, double height);

    protected Brush ThemeBrush(string key) => (Brush)FindResource(key);

    /// <summary>Record the describing text for a chart section. The shared tooltip reads it on hover
    /// (stored in <see cref="FrameworkElement.Tag"/> rather than the framework ToolTip property).</summary>
    protected void SetTooltip(FrameworkElement element, string text) => element.Tag = text;

    // ── Manual hover tooltip ────────────────────────────────────────────────────────────────────────

    private void OnChartMouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(Root);
        var text = HitText(pos);
        if (text is null) { HideTip(); return; }

        // Position above-right of the pointer so it never sits under the cursor or the section.
        _tip.HorizontalOffset = pos.X + 14;
        _tip.VerticalOffset = pos.Y - 34;

        if (_tip.IsOpen)
        {
            // Already showing: switch to the new section's text instantly.
            if (!ReferenceEquals(text, _shownText) && text != _shownText)
            {
                _shownText = text;
                _tip.Content = text;
            }
        }
        else
        {
            _shownText = text;
            _tip.Content = text;
            if (!_showTimer.IsEnabled)
            {
                _showTimer.Interval = TimeSpan.FromMilliseconds(_vm?.TooltipDelayMs ?? 250);
                _showTimer.Start();
            }
        }
    }

    // The describing text of the section under the point, or null over empty space / labels / axes.
    private string? HitText(Point p)
    {
        DependencyObject? d = Root.InputHitTest(p) as DependencyObject;
        while (d is not null)
        {
            if (d is FrameworkElement fe && fe.Tag is string s) return s;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private void HideTip()
    {
        _showTimer.Stop();
        _tip.IsOpen = false;
        _shownText = null;
    }

    protected void DrawCenteredMessage(string text)
    {
        var tb = new TextBlock { Text = text, Foreground = ThemeBrush("SubtleBrush"), FontSize = 13 };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tb, (ActualWidth - tb.DesiredSize.Width) / 2);
        Canvas.SetTop(tb, (ActualHeight - tb.DesiredSize.Height) / 2);
        Root.Children.Add(tb);
    }

    protected static string Humanize(long ms) => TimeFormat.Humanize(ms);
}
