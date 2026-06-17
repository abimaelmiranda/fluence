using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Fluence.Desktop.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        var version = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        VersionText.Text = !string.IsNullOrWhiteSpace(version)
            ? $"Version {version}"
            : "Version 0.1.0-alpha";
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
