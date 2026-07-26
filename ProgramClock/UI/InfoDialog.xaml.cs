using System.Windows;

namespace ProgramClock.UI;

/// <summary>A themed one-time popup (introduction on first run, or patch notes after an update): a
/// title, an intro line, a bulleted list, and an accept button that closes it.</summary>
public partial class InfoDialog : Window
{
    public InfoDialog(string title, string intro, IReadOnlyList<string> bullets)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        IntroText.Text = intro;
        Bullets.ItemsSource = bullets;
    }

    private void OnOk(object sender, RoutedEventArgs e) => Close();
}
