using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace ProgramClock.UI.Theme;

/// <summary>
/// Applies the Windows light/dark app theme and swaps it live when the user changes the
/// OS setting. The theme dictionary lives at slot 0 of the application's merged dictionaries;
/// shared control styles sit after it and reference the swappable brushes via DynamicResource.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    private static readonly Uri LightUri = new("UI/Theme/Light.xaml", UriKind.Relative);
    private static readonly Uri DarkUri = new("UI/Theme/Dark.xaml", UriKind.Relative);
    private static readonly Uri ControlsUri = new("UI/Theme/Controls.xaml", UriKind.Relative);

    private bool _isLight;

    // Overrides AccentBrush/AccentColor with the user's chosen colour. Appended after the theme and
    // control dictionaries so its keys win for every DynamicResource lookup; survives live theme swaps.
    private ResourceDictionary? _accentDict;

    // Overrides the background/surface/text/border palette derived from the user's chosen main colour.
    private ResourceDictionary? _mainDict;

    public void Initialize()
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        _isLight = IsSystemLightTheme();
        dicts.Add(new ResourceDictionary { Source = _isLight ? LightUri : DarkUri });
        dicts.Add(new ResourceDictionary { Source = ControlsUri });
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        var light = IsSystemLightTheme();
        if (light == _isLight) return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            _isLight = light;
            var dicts = Application.Current.Resources.MergedDictionaries;
            dicts[0] = new ResourceDictionary { Source = _isLight ? LightUri : DarkUri };
        });
    }

    /// <summary>
    /// Recolours the accent used throughout the app. Pass null (or an unparseable value) to fall back
    /// to the theme's built-in red. The override sits at the end of the merged dictionaries, so it
    /// takes precedence and persists across light/dark swaps.
    /// </summary>
    public void ApplyAccent(string? hex)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        if (_accentDict is not null)
        {
            dicts.Remove(_accentDict);
            _accentDict = null;
        }

        if (string.IsNullOrWhiteSpace(hex) || !TryParseColor(hex, out var color))
            return;

        _accentDict = new ResourceDictionary
        {
            ["AccentColor"] = color,
            ["AccentBrush"] = new SolidColorBrush(color),
        };
        dicts.Add(_accentDict);
    }

    /// <summary>
    /// Recolours the whole app's background palette from a single main colour, deriving a readable
    /// text colour (by luminance) plus matching surface, subtle and border shades. Pass null to fall
    /// back to the system light/dark theme. The override is appended last so it wins, and persists
    /// across OS theme swaps.
    /// </summary>
    public void ApplyMainColor(string? hex)
    {
        var dicts = Application.Current.Resources.MergedDictionaries;
        if (_mainDict is not null)
        {
            dicts.Remove(_mainDict);
            _mainDict = null;
        }

        if (string.IsNullOrWhiteSpace(hex) || !TryParseColor(hex, out var bg))
            return;

        bool isDark = Luminance(bg) < 0.5;
        var fg = isDark ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Color.FromRgb(0x1A, 0x1A, 0x1A);
        var surface = Blend(bg, Colors.White, isDark ? 0.10 : 0.04);
        var subtle = Blend(bg, fg, 0.55);
        var border = Blend(bg, fg, 0.22);

        _mainDict = new ResourceDictionary
        {
            ["WindowBackgroundBrush"] = new SolidColorBrush(bg),
            ["SurfaceBrush"] = new SolidColorBrush(surface),
            ["ForegroundBrush"] = new SolidColorBrush(fg),
            ["SubtleBrush"] = new SolidColorBrush(subtle),
            ["BorderBrush"] = new SolidColorBrush(border),
        };
        dicts.Add(_mainDict);
    }

    /// <summary>Perceived brightness of a colour, 0 (black) to 1 (white).</summary>
    private static double Luminance(Color c) =>
        (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    /// <summary>Linear blend from <paramref name="a"/> toward <paramref name="b"/> by fraction t (0–1).</summary>
    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static bool TryParseColor(string hex, out Color color)
    {
        try
        {
            color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    private static bool IsSystemLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(AppsUseLightThemeValue) is int v ? v != 0 : true;
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
