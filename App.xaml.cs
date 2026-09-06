using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace GofileUploader;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Use a fixed accent (Apple system-blue) instead of the user's Windows accent,
        // so the app looks the same everywhere and matches our custom styles.
        ApplicationAccentColorManager.Apply(Color.FromRgb(0x0A, 0x84, 0xFF), ApplicationTheme.Dark);
    }
}

