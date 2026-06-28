using System.Windows.Media;
// WinForms implicit usings pull System.Drawing into scope; pin these to the WPF media types.
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace ProgramClock.UI.Charts;

/// <summary>Generates stable, distinct colours for chart series. Categories get evenly-spread hues
/// (golden-angle stepping); the apps within a category get graded shades of that same hue, so an
/// app reads as "part of" its category in both the bar segments and the donut's inner ring.</summary>
public static class ChartPalette
{
    // Golden angle keeps successive hues far apart without ever repeating for any practical count.
    private const double GoldenAngle = 137.508;
    private const double StartHue = 8.0;   // start near the app's signature red
    private const double CategorySaturation = 0.55;
    private const double CategoryValue = 0.85;

    /// <summary>A category's base colour by rank (0-based).</summary>
    public static Brush Category(int index) =>
        Frozen(HsvToRgb((StartHue + index * GoldenAngle) % 360.0, CategorySaturation, CategoryValue));

    /// <summary>A shade of the category colour for one app, darkening with the app's rank within the
    /// category so the highest-time apps read brightest. Hue is kept so apps stay visually grouped.</summary>
    public static Brush AppShade(int categoryIndex, int appRank, int appCount)
    {
        double hue = (StartHue + categoryIndex * GoldenAngle) % 360.0;
        // Step value down per rank, but never so far it disappears on a dark background.
        double step = appCount > 1 ? 0.30 / (appCount - 1) : 0;
        double value = Math.Clamp(CategoryValue - appRank * step, 0.45, 0.95);
        // Nudge saturation up slightly for lower apps so they stay distinguishable as they darken.
        double sat = Math.Clamp(CategorySaturation + appRank * 0.04, 0.0, 0.85);
        return Frozen(HsvToRgb(hue, sat, value));
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // HSV (h 0..360, s/v 0..1) -> RGB. Mirrors the helper in UI/Controls/ColorPicker.xaml.cs.
    private static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;

        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
