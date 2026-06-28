using System.Windows.Controls;
// Pin WPF types over their System.Drawing / WinForms namesakes.
using Rectangle = System.Windows.Shapes.Rectangle;
using Line = System.Windows.Shapes.Line;
using Size = System.Windows.Size;
using Brush = System.Windows.Media.Brush;
using DependencyObject = System.Windows.DependencyObject;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using VisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace ProgramClock.UI.Charts;

/// <summary>Per-day vertical bars across the selected range, with a labelled Y axis so the bar
/// heights are readable as durations. Bar height is proportional to the busiest day; X labels thin
/// out automatically when the range spans many days. Hovering a bar grows it slightly (wider and a
/// little taller) when the grow setting is on.</summary>
public sealed class TrendChartView : ChartHost
{
    private const int YTicks = 4;
    private const double Grow = 4.0;   // px the hovered bar expands by

    private readonly Dictionary<DependencyObject, int> _map = new();
    private int? _hover;

    public TrendChartView()
    {
        MouseMove += OnMove;
        MouseLeave += OnLeave;
    }

    protected override bool IsEmpty(ChartSnapshot data) => data.Days.Count == 0 || data.Max <= 0;

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!GrowOnHover) return;
        int? day = null;
        if (Root.InputHitTest(e.GetPosition(Root)) is DependencyObject hit)
            for (var d = hit; d is not null; d = VisualTreeHelper.GetParent(d))
                if (_map.TryGetValue(d, out var v)) { day = v; break; }
        if (day != _hover) { _hover = day; RenderNow(); }
    }

    private void OnLeave(object sender, MouseEventArgs e)
    {
        if (_hover is not null) { _hover = null; RenderNow(); }
    }

    protected override void Draw(ChartSnapshot data, double width, double height)
    {
        _map.Clear();
        const double topPad = 12, bottomPad = 26, rightPad = 12, labelGap = 8;
        var axisBrush = ThemeBrush("BorderBrush");
        var labelBrush = ThemeBrush("SubtleBrush");

        // Pre-measure the Y tick labels so the left gutter is wide enough to never clip them.
        var tickLabels = new TextBlock[YTicks + 1];
        double maxLabelW = 0;
        for (int t = 0; t <= YTicks; t++)
        {
            var lbl = new TextBlock
            {
                Text = Humanize((long)Math.Round(data.Max * ((double)t / YTicks))),
                Foreground = labelBrush,
                FontSize = 10,
            };
            lbl.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            maxLabelW = Math.Max(maxLabelW, lbl.DesiredSize.Width);
            tickLabels[t] = lbl;
        }

        double leftPad = maxLabelW + labelGap * 2;   // gutter sized to the widest label
        double plotW = width - leftPad - rightPad;
        double plotH = height - topPad - bottomPad;
        if (plotW <= 0 || plotH <= 0) return;

        double baseY = topPad + plotH;

        // Y axis: gridlines + the pre-measured duration labels (0 up to the busiest day).
        for (int t = 0; t <= YTicks; t++)
        {
            double frac = (double)t / YTicks;
            double y = baseY - plotH * frac;

            Root.Children.Add(new Line
            {
                X1 = leftPad,
                Y1 = y,
                X2 = leftPad + plotW,
                Y2 = y,
                Stroke = axisBrush,
                StrokeThickness = t == 0 ? 1 : 0.5,
                Opacity = t == 0 ? 1.0 : 0.35,
            });

            var lbl = tickLabels[t];
            Canvas.SetLeft(lbl, leftPad - labelGap - lbl.DesiredSize.Width);
            Canvas.SetTop(lbl, y - lbl.DesiredSize.Height / 2);
            Root.Children.Add(lbl);
        }

        // Vertical (Y) axis line.
        Root.Children.Add(new Line
        {
            X1 = leftPad,
            Y1 = topPad,
            X2 = leftPad,
            Y2 = baseY,
            Stroke = axisBrush,
            StrokeThickness = 1,
        });

        int n = data.Days.Count;
        double slot = plotW / n;
        double barW = Math.Min(48, slot * 0.7);
        int labelEvery = Math.Max(1, (int)Math.Ceiling(n / 12.0));
        var fill = ThemeBrush("AccentBrush");

        bool grow = GrowOnHover;
        int hd = grow ? (_hover ?? -1) : -1;
        bool haveOv = false;
        double ovX = 0, ovH = 0;
        string ovTip = "";

        for (int i = 0; i < n; i++)
        {
            var d = data.Days[i];
            double barH = data.Max > 0 ? plotH * d.Value / data.Max : 0;
            double x = leftPad + slot * i + (slot - barW) / 2;
            string tip = $"{d.Label}\n{Humanize(d.Value)} {MetricName}";

            var rect = new Rectangle
            {
                Width = barW,
                Height = Math.Max(0, barH),
                Fill = fill,
                RadiusX = 2,
                RadiusY = 2,
            };
            SetTooltip(rect, tip);
            _map[rect] = i;
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, baseY - barH);
            Root.Children.Add(rect);

            if (hd == i) { haveOv = true; ovX = x; ovH = barH; ovTip = tip; }

            if (i % labelEvery == 0)
            {
                var tb = new TextBlock { Text = d.Label, Foreground = labelBrush, FontSize = 10 };
                Canvas.SetLeft(tb, leftPad + slot * i);
                Canvas.SetTop(tb, baseY + 6);
                Root.Children.Add(tb);
            }
        }

        // Hovered bar overlay: wider on each side and a little taller, kept anchored to the baseline.
        if (haveOv)
        {
            var ov = new Rectangle
            {
                Width = barW + 2 * Grow,
                Height = Math.Max(0, ovH + Grow),
                Fill = fill,
                RadiusX = 2,
                RadiusY = 2,
            };
            SetTooltip(ov, ovTip);
            _map[ov] = hd;
            Canvas.SetLeft(ov, ovX - Grow);
            Canvas.SetTop(ov, baseY - (ovH + Grow));
            Root.Children.Add(ov);
        }
    }
}
