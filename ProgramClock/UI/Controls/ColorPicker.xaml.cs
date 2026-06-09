using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
// Disambiguate the WPF types from their System.Drawing / System.Windows.Forms namesakes, which the
// WinForms implicit usings also pull into scope (UseWindowsForms is enabled for the tray icon).
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;

namespace ProgramClock.UI.Controls;

/// <summary>
/// A Windows-style colour picker: a saturation/value field, a hue slider, and a hex input with a
/// live preview swatch. Exposes <see cref="SelectedColor"/> and raises <see cref="SelectedColorChanged"/>
/// while the user drags or types. The control works in HSV internally so dragging stays smooth.
/// </summary>
public partial class ColorPicker : UserControl
{
    // Current colour in HSV: hue 0–360, saturation/value 0–1.
    private double _h;
    private double _s;
    private double _v;

    // Guards against the SelectedColor setter re-entering the UI update while we're the ones changing it.
    private bool _updating;
    private bool _svCaptured;
    private bool _hueCaptured;

    public ColorPicker()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateUi(raise: false);
    }

    /// <summary>Raised whenever the colour changes through user interaction (drag or hex entry).</summary>
    public event EventHandler? SelectedColorChanged;

    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(Color), typeof(ColorPicker),
        new FrameworkPropertyMetadata(Colors.Red, OnSelectedColorChanged));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ColorPicker)d;
        if (picker._updating) return;            // change came from our own UI; nothing to sync back
        var c = (Color)e.NewValue;
        RgbToHsv(c, out picker._h, out picker._s, out picker._v);
        picker.UpdateUi(raise: false);
    }

    // ── Saturation / value field ──────────────────────────────────────────────────────────────────

    private void OnSvMouseDown(object sender, MouseButtonEventArgs e)
    {
        _svCaptured = true;
        SvBox.CaptureMouse();
        UpdateSvFromPoint(e.GetPosition(SvBox));
    }

    private void OnSvMouseMove(object sender, MouseEventArgs e)
    {
        if (_svCaptured && e.LeftButton == MouseButtonState.Pressed)
            UpdateSvFromPoint(e.GetPosition(SvBox));
    }

    private void OnSvMouseUp(object sender, MouseButtonEventArgs e)
    {
        _svCaptured = false;
        SvBox.ReleaseMouseCapture();
    }

    private void UpdateSvFromPoint(Point p)
    {
        double w = SvBox.ActualWidth, h = SvBox.ActualHeight;
        if (w <= 0 || h <= 0) return;
        _s = Math.Clamp(p.X / w, 0, 1);
        _v = Math.Clamp(1 - p.Y / h, 0, 1);
        UpdateUi(raise: true);
    }

    // ── Hue slider ────────────────────────────────────────────────────────────────────────────────

    private void OnHueMouseDown(object sender, MouseButtonEventArgs e)
    {
        _hueCaptured = true;
        HueBar.CaptureMouse();
        UpdateHueFromPoint(e.GetPosition(HueBar));
    }

    private void OnHueMouseMove(object sender, MouseEventArgs e)
    {
        if (_hueCaptured && e.LeftButton == MouseButtonState.Pressed)
            UpdateHueFromPoint(e.GetPosition(HueBar));
    }

    private void OnHueMouseUp(object sender, MouseButtonEventArgs e)
    {
        _hueCaptured = false;
        HueBar.ReleaseMouseCapture();
    }

    private void UpdateHueFromPoint(Point p)
    {
        double w = HueBar.ActualWidth;
        if (w <= 0) return;
        _h = Math.Clamp(p.X / w, 0, 1) * 360;
        UpdateUi(raise: true);
    }

    // ── Hex entry ─────────────────────────────────────────────────────────────────────────────────

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitHex();
    }

    private void OnHexLostFocus(object sender, RoutedEventArgs e) => CommitHex();

    private void CommitHex()
    {
        var text = HexBox.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(text);
            RgbToHsv(c, out _h, out _s, out _v);
            UpdateUi(raise: true);
        }
        catch
        {
            // Invalid hex: restore the box to the current colour.
            HexBox.Text = ToHex(HsvToRgb(_h, _s, _v));
        }
    }

    // ── Shared UI sync ────────────────────────────────────────────────────────────────────────────

    private void UpdateUi(bool raise)
    {
        var color = HsvToRgb(_h, _s, _v);

        // Repaint the SV field's base hue and move the thumbs.
        SvHueRect.Fill = new SolidColorBrush(HsvToRgb(_h, 1, 1));
        PreviewSwatch.Background = new SolidColorBrush(color);

        double sw = SvBox.ActualWidth, sh = SvBox.ActualHeight;
        if (sw > 0 && sh > 0)
        {
            Canvas.SetLeft(SvThumb, _s * sw - SvThumb.Width / 2);
            Canvas.SetTop(SvThumb, (1 - _v) * sh - SvThumb.Height / 2);
        }
        double hw = HueBar.ActualWidth;
        if (hw > 0)
            Canvas.SetLeft(HueThumb, _h / 360 * hw - HueThumb.Width / 2);

        // Don't stomp the hex box while the user is typing in it.
        if (!HexBox.IsKeyboardFocused)
            HexBox.Text = ToHex(color);

        _updating = true;
        SelectedColor = color;
        _updating = false;

        if (raise) SelectedColorChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Colour maths ──────────────────────────────────────────────────────────────────────────────

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static void RgbToHsv(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        v = max;
        s = max <= 0 ? 0 : delta / max;

        if (delta <= 0) { h = 0; return; }

        if (max == r) h = 60 * (((g - b) / delta) % 6);
        else if (max == g) h = 60 * ((b - r) / delta + 2);
        else h = 60 * ((r - g) / delta + 4);

        if (h < 0) h += 360;
    }

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
