using System.Windows;
using Microsoft.Win32;
using Application = System.Windows.Application;

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

    private static bool IsSystemLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(AppsUseLightThemeValue) is int v ? v != 0 : true;
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
