using System.Windows;
using System.Windows.Controls;
// Pin WPF types over their System.Drawing / WinForms namesakes.
using Rectangle = System.Windows.Shapes.Rectangle;
using Brush = System.Windows.Media.Brush;
using DependencyObject = System.Windows.DependencyObject;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using VisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace ProgramClock.UI.Charts;

/// <summary>Hierarchical horizontal bars: one bar per category (ranked by total), each subdivided into
/// stacked segments for its apps (ranked within the category). Bar length is the category's share of
/// the largest category; segment widths split that bar by app value. Hovering a segment grows it
/// slightly (popping out on every side) when the grow setting is on.</summary>
public sealed class BarChartView : ChartHost
{
    private const double Grow = 4.0;   // px the hovered segment expands on each side

    private readonly Dictionary<DependencyObject, (int Cat, int App)> _map = new();
    private (int Cat, int App)? _hover;

    public BarChartView()
    {
        MouseMove += OnMove;
        MouseLeave += OnLeave;
    }

    protected override bool IsEmpty(ChartSnapshot data) => data.Categories.Count == 0 || data.Max <= 0;

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!GrowOnHover) return;
        (int, int)? section = null;
        if (Root.InputHitTest(e.GetPosition(Root)) is DependencyObject hit)
            for (var d = hit; d is not null; d = VisualTreeHelper.GetParent(d))
                if (_map.TryGetValue(d, out var v)) { section = v; break; }
        if (!Nullable.Equals(section, _hover)) { _hover = section; RenderNow(); }
    }

    private void OnLeave(object sender, MouseEventArgs e)
    {
        if (_hover is not null) { _hover = null; RenderNow(); }
    }

    protected override void Draw(ChartSnapshot data, double width, double height)
    {
        _map.Clear();
        const double leftPad = 12, rightPad = 12, barH = 18, labelH = 18, gap = 12, top = 8;
        double trackW = width - leftPad - rightPad;
        if (trackW <= 0) return;

        bool grow = GrowOnHover;
        int hc = grow ? (_hover?.Cat ?? -1) : -1;
        int ha = grow ? (_hover?.App ?? -1) : -1;

        // Captured geometry of the hovered segment, redrawn enlarged on top after the base pass.
        bool haveOv = false;
        double ovX = 0, ovY = 0, ovW = 0;
        Brush? ovFill = null;
        (int, int) ovKey = default;
        string ovTip = "";

        double y = top;
        for (int i = 0; i < data.Categories.Count; i++)
        {
            var cat = data.Categories[i];
            var label = new TextBlock
            {
                Text = $"{cat.Name}  ·  {Humanize(cat.Value)}  ({cat.Percent:P0})",
                Foreground = ThemeBrush("ForegroundBrush"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
            };
            Canvas.SetLeft(label, leftPad);
            Canvas.SetTop(label, y);
            Root.Children.Add(label);
            y += labelH;

            double barW = data.Max > 0 ? trackW * cat.Value / data.Max : 0;
            double segTotal = 0;
            foreach (var a in cat.Apps) segTotal += a.Value;

            double x = leftPad;
            for (int j = 0; j < cat.Apps.Count; j++)
            {
                var app = cat.Apps[j];
                double segW = segTotal > 0 ? barW * app.Value / segTotal : 0;
                var seg = new Rectangle { Width = Math.Max(0, segW), Height = barH, Fill = app.Color };
                SetTooltip(seg, AppTip(app, cat));
                _map[seg] = (i, j);
                Canvas.SetLeft(seg, x);
                Canvas.SetTop(seg, y);
                Root.Children.Add(seg);

                if (hc == i && ha == j)
                {
                    haveOv = true; ovX = x; ovY = y; ovW = segW;
                    ovFill = app.Color; ovKey = (i, j); ovTip = AppTip(app, cat);
                }
                x += segW;
            }

            // Outline around the whole category bar so a single-app category still reads as a bar.
            var outline = new Border
            {
                Width = Math.Max(0, barW),
                Height = barH,
                BorderBrush = ThemeBrush("BorderBrush"),
                BorderThickness = new Thickness(0.7),
                CornerRadius = new CornerRadius(2),
            };
            Canvas.SetLeft(outline, leftPad);
            Canvas.SetTop(outline, y);
            Root.Children.Add(outline);

            y += barH + gap;
        }

        // Hovered segment overlay: grown on every side and drawn on top of the row (and its outline).
        if (haveOv && ovFill is not null)
        {
            var ov = new Rectangle
            {
                Width = Math.Max(0, ovW + 2 * Grow),
                Height = barH + 2 * Grow,
                Fill = ovFill,
                RadiusX = 2,
                RadiusY = 2,
            };
            SetTooltip(ov, ovTip);
            _map[ov] = ovKey;
            Canvas.SetLeft(ov, ovX - Grow);
            Canvas.SetTop(ov, ovY - Grow);
            Root.Children.Add(ov);
        }
    }

    private static string AppTip(AppSlice app, CategorySlice cat) =>
        $"{app.DisplayName}\nCategory: {cat.Name}\n{Humanize(app.Value)}  ·  {app.Percent:P0} of {cat.Name}";
}
