using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ProgramClock.UI;

/// <summary>Turns a 0..1 fraction into a stroked arc that sweeps clockwise from 12 o'clock,
/// for the auto-refresh countdown ring. ConverterParameter is the radius (default 7).</summary>
public sealed class RefreshArcConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        double frac = value is double d ? d : 0;
        frac = Math.Clamp(frac, 0, 1);

        double r = 7;
        if (parameter is string ps &&
            double.TryParse(ps, NumberStyles.Any, CultureInfo.InvariantCulture, out var pr))
            r = pr;

        var geo = new StreamGeometry();
        if (frac > 0)
        {
            double cx = r, cy = r;
            double angle = Math.Min(frac * 360.0, 359.999);
            double rad = Math.PI / 180.0 * angle;
            var start = new Point(cx, cy - r);
            var end = new Point(cx + r * Math.Sin(rad), cy - r * Math.Cos(rad));
            using var ctx = geo.Open();
            ctx.BeginFigure(start, isFilled: false, isClosed: false);
            ctx.ArcTo(end, new Size(r, r), 0, angle > 180,
                SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }
        geo.Freeze();
        return geo;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
