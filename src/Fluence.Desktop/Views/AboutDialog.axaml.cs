using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Fluence.Core.Services.Localization;

namespace Fluence.Desktop.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        var version = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var prefix = Locale.Current["Desktop.About.VersionPrefix"];
        VersionText.Text = !string.IsNullOrWhiteSpace(version)
            ? string.Format(prefix, version)
            : string.Format(prefix, "0.1.0-alpha");
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
