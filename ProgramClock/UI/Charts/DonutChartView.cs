using System.Windows.Controls;
using System.Windows.Media;
// Pin WPF types over their System.Drawing / WinForms namesakes.
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Path = System.Windows.Shapes.Path;
using Rectangle = System.Windows.Shapes.Rectangle;
using TextTrimming = System.Windows.TextTrimming;
using FontWeights = System.Windows.FontWeights;
using Thickness = System.Windows.Thickness;
using Orientation = System.Windows.Controls.Orientation;
using ScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;
using DependencyObject = System.Windows.DependencyObject;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using VisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace ProgramClock.UI.Charts;

/// <summary>Two-ring sunburst: the outer ring is categories (arc proportional to category total), the
/// inner ring is apps, each app's arc nested inside its parent category's angular span. A legend lists
/// the categories with their percentages.
/// <para>
/// Hovering a section emphasises only that section: every wedge is always drawn at its fixed position,
/// then the hovered one is drawn again on top — grown ~1° on each side and popped outward a few pixels.
/// Because the other wedges are never recomputed, none can shrink to nothing or shift around; the
/// overlay just overlaps its neighbours slightly, which reads as the section growing into them.
/// </para></summary>
public sealed class DonutChartView : ChartHost
{
    private const double GrowDeg = 3.0;     // angular growth per side for the hovered section
    private const double PopPx = 8.0;       // radial pop (exploded-slice offset) for the hovered section
    private const int WholeCategory = -2;   // App sentinel: grow the whole category (legend hover)

    // Maps each drawn element to its (category, app) identity; app = -1 marks an outer category wedge,
    // app = WholeCategory (-2) marks a legend entry that grows the whole category.
    private readonly Dictionary<DependencyObject, (int Cat, int App)> _map = new();

    // The section currently hovered (drives the overlay); null when nothing is hovered.
    private (int Cat, int App)? _hover;

    public DonutChartView()
    {
        MouseMove += OnDonutMouseMove;
        MouseLeave += OnDonutMouseLeave;
    }

    protected override bool IsEmpty(ChartSnapshot data) => data.Categories.Count == 0 || data.Total <= 0;

    private void OnDonutMouseMove(object sender, MouseEventArgs e)
    {
        if (!GrowOnHover) return;   // grow disabled: don't track hover or re-render

        (int, int)? section = null;
        if (Root.InputHitTest(e.GetPosition(Root)) is DependencyObject hit)
        {
            for (var d = hit; d is not null; d = VisualTreeHelper.GetParent(d))
                if (_map.TryGetValue(d, out var v)) { section = v; break; }
        }
        if (!Nullable.Equals(section, _hover))
        {
            _hover = section;
            RenderNow();
        }
    }

    private void OnDonutMouseLeave(object sender, MouseEventArgs e)
    {
        if (_hover is not null)
        {
            _hover = null;
            RenderNow();
        }
    }

    protected override void Draw(ChartSnapshot data, double width, double height)
    {
        _map.Clear();

        double legendW = Math.Min(220, Math.Max(120, width * 0.40));
        double chartW = width - legendW;
        double size = Math.Min(chartW, height) - 16 - PopPx * 2;   // leave room for the popped slice
        if (size < 60) return;

        double cx = 8 + PopPx + size / 2;
        double cy = height / 2;
        double rOuter = size / 2;
        double rCatInner = rOuter * 0.62;   // outer band = categories
        double rAppOuter = rCatInner - 2;
        double rAppInner = rOuter * 0.30;   // center hole

        double total = data.Total;
        int n = data.Categories.Count;
        // Hover state. App >= 0 => one app; App == -1 => one category wedge; App == WholeCategory => the
        // whole category (every section under it), triggered by hovering its legend entry. Grow
        // disabled => nothing hovered.
        bool grow = GrowOnHover;
        int hc = grow ? (_hover?.Cat ?? -1) : -1;
        int ha = grow ? (_hover?.App ?? -1) : -1;

        double hcA0 = 0, hcSweep = 0;   // base angles of the hovered category, for the overlay pass

        double catAcc = 0;
        for (int i = 0; i < n; i++)
        {
            var cat = data.Categories[i];
            double a0 = catAcc / total * 360.0;
            double sweep = cat.Value / total * 360.0;
            catAcc += cat.Value;
            if (i == hc) { hcA0 = a0; hcSweep = sweep; }

            // Skip the base outer wedge when this category's wedge — or its whole category — is hovered;
            // the overlay drawn afterwards replaces it.
            if (!(i == hc && (ha == -1 || ha == WholeCategory)))
            {
                var catWedge = Wedge(cx, cy, rCatInner, rOuter, a0, a0 + sweep, cat.Color);
                SetTooltip(catWedge, CategoryTip(cat));
                _map[catWedge] = (i, -1);
                Root.Children.Add(catWedge);
            }

            int m = cat.Apps.Count;
            double appAcc = 0;
            for (int j = 0; j < m; j++)
            {
                var app = cat.Apps[j];
                double b0 = a0 + (cat.Value > 0 ? appAcc / cat.Value * sweep : 0);
                appAcc += app.Value;
                double b1 = a0 + (cat.Value > 0 ? appAcc / cat.Value * sweep : 0);

                if (!(i == hc && (ha == j || ha == WholeCategory)))
                {
                    var appWedge = Wedge(cx, cy, rAppInner, rAppOuter, b0, b1, app.Color);
                    SetTooltip(appWedge, AppTip(app, cat));
                    _map[appWedge] = (i, j);
                    Root.Children.Add(appWedge);
                }
            }
        }

        // Overlay pass: redraw the hovered thing on top, grown ±GrowDeg and popped by extending the
        // outer radius (uniform along the whole arc — works for any slice size).
        if (hc >= 0 && hc < n)
        {
            var cat = data.Categories[hc];
            if (ha == WholeCategory)
            {
                // The whole category grows as one: the outer wedge plus every app slice, over the grown
                // span, popped outward together.
                double g0 = hcA0 - GrowDeg, g1 = hcA0 + hcSweep + GrowDeg, span = g1 - g0;
                AddOverlay(Wedge(cx, cy, rCatInner, rOuter + PopPx, g0, g1, cat.Color), (hc, WholeCategory), CategoryTip(cat));
                double acc = 0;
                foreach (var app in cat.Apps)
                {
                    double b0 = g0 + (cat.Value > 0 ? acc / cat.Value * span : 0);
                    acc += app.Value;
                    double b1 = g0 + (cat.Value > 0 ? acc / cat.Value * span : 0);
                    AddOverlay(Wedge(cx, cy, rAppInner, rAppOuter, b0, b1, app.Color), (hc, WholeCategory), AppTip(app, cat));
                }
            }
            else if (ha == -1)
            {
                AddOverlay(Wedge(cx, cy, rCatInner, rOuter + PopPx, hcA0 - GrowDeg, hcA0 + hcSweep + GrowDeg, cat.Color),
                    (hc, -1), CategoryTip(cat));
            }
            else if (ha >= 0 && ha < cat.Apps.Count)
            {
                double before = 0;
                for (int k = 0; k < ha; k++) before += cat.Apps[k].Value;
                double b0 = hcA0 + (cat.Value > 0 ? before / cat.Value * hcSweep : 0);
                double b1 = hcA0 + (cat.Value > 0 ? (before + cat.Apps[ha].Value) / cat.Value * hcSweep : 0);
                AddOverlay(Wedge(cx, cy, rAppInner, rAppOuter + PopPx, b0 - GrowDeg, b1 + GrowDeg, cat.Apps[ha].Color),
                    (hc, ha), AppTip(cat.Apps[ha], cat));
            }
        }

        // When a legend key is hovered (whole category), expand its apps as a temporary sublist.
        int expandedCat = ha == WholeCategory ? hc : -1;
        DrawLegend(data, width - legendW + 8, 12, legendW - 16, expandedCat, height - 8);
    }

    private void AddOverlay(Path wedge, (int, int) key, string tip)
    {
        SetTooltip(wedge, tip);
        _map[wedge] = key;
        Root.Children.Add(wedge);
    }

    private static string CategoryTip(CategorySlice cat) =>
        $"{cat.Name}  (category)\n{Humanize(cat.Value)}  ·  {cat.Percent:P0} of total";

    private static string AppTip(AppSlice app, CategorySlice cat) =>
        $"{app.DisplayName}\nCategory: {cat.Name}\n{Humanize(app.Value)}  ·  {app.Percent:P0} of {cat.Name}";

    private Path Wedge(double cx, double cy, double ri, double ro, double a0, double a1, Brush fill)
    {
        if (a1 - a0 >= 360) a1 = a0 + 359.999;   // avoid a degenerate full-circle arc
        bool large = (a1 - a0) > 180;
        var p0o = Polar(cx, cy, ro, a0);
        var p1o = Polar(cx, cy, ro, a1);
        var p1i = Polar(cx, cy, ri, a1);
        var p0i = Polar(cx, cy, ri, a0);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(p0o, isFilled: true, isClosed: true);
            ctx.ArcTo(p1o, new Size(ro, ro), 0, large, SweepDirection.Clockwise, true, false);
            ctx.LineTo(p1i, true, false);
            ctx.ArcTo(p0i, new Size(ri, ri), 0, large, SweepDirection.Counterclockwise, true, false);
        }
        geo.Freeze();

        return new Path
        {
            Data = geo,
            Fill = fill,
            Stroke = ThemeBrush("WindowBackgroundBrush"),
            StrokeThickness = 1,
        };
    }

    // Polar -> Cartesian with 0 deg at 12 o'clock, increasing clockwise (matches RefreshArcConverter).
    private static Point Polar(double cx, double cy, double r, double angleDeg)
    {
        double rad = Math.PI / 180.0 * angleDeg;
        return new Point(cx + r * Math.Sin(rad), cy - r * Math.Cos(rad));
    }

    private void DrawLegend(ChartSnapshot data, double x, double y, double w, int expandedCat, double bottomLimit)
    {
        int count = data.Categories.Count;
        for (int i = 0; i < count; i++)
        {
            var cat = data.Categories[i];
            string tip = CategoryTip(cat);

            // Each legend entry maps to its whole category, so hovering it grows the entire category.
            var swatch = new Rectangle { Width = 12, Height = 12, Fill = cat.Color };
            SetTooltip(swatch, tip);
            _map[swatch] = (i, WholeCategory);
            Canvas.SetLeft(swatch, x);
            Canvas.SetTop(swatch, y + 2);
            Root.Children.Add(swatch);

            var tb = new TextBlock
            {
                Text = $"{cat.Name}  {cat.Percent:P0}",
                Foreground = ThemeBrush("ForegroundBrush"),
                FontSize = 12,
                FontWeight = i == expandedCat ? FontWeights.SemiBold : FontWeights.Normal,
                MaxWidth = Math.Max(20, w - 18),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            SetTooltip(tb, tip);
            _map[tb] = (i, WholeCategory);
            Canvas.SetLeft(tb, x + 18);
            Canvas.SetTop(tb, y);
            Root.Children.Add(tb);
            y += 20;

            // Temporary sublist of the hovered category's apps, indented under its key.
            if (i == expandedCat)
            {
                const double indent = 22;
                double listW = Math.Max(20, w - indent);

                var list = new StackPanel { Width = listW };
                foreach (var app in cat.Apps)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                    row.Children.Add(new Rectangle { Width = 9, Height = 9, Fill = app.Color, Margin = new Thickness(0, 3, 6, 0) });
                    row.Children.Add(new TextBlock
                    {
                        Text = $"{app.DisplayName}  {app.Percent:P0}",
                        Foreground = ThemeBrush("SubtleBrush"),
                        FontSize = 11,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });
                    SetTooltip(row, AppTip(app, cat));   // hovering a row keeps the whole category active
                    list.Children.Add(row);
                }

                list.Measure(new Size(listW, double.PositiveInfinity));
                double natural = list.DesiredSize.Height;

                // Reserve room for the category keys still to be drawn below, so they aren't pushed off
                // the bottom; if the sublist doesn't fit in what's left, scroll it instead.
                double reserve = (count - 1 - i) * 20;
                double avail = bottomLimit - y - reserve;

                if (natural <= avail)
                {
                    Canvas.SetLeft(list, x + indent);
                    Canvas.SetTop(list, y);
                    _map[list] = (i, WholeCategory);
                    Root.Children.Add(list);
                    y += natural + 3;
                }
                else
                {
                    double boxH = Math.Max(40, avail);
                    var sv = new ScrollViewer
                    {
                        Content = list,
                        Width = listW,
                        Height = boxH,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    };
                    _map[sv] = (i, WholeCategory);
                    Canvas.SetLeft(sv, x + indent);
                    Canvas.SetTop(sv, y);
                    Root.Children.Add(sv);
                    y += boxH + 3;
                }
            }
        }
    }
}
