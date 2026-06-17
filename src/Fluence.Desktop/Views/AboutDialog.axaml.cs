using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Fluence.Desktop.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = version is not null
            ? $"Version {version.Major}.{version.Minor}.{version.Build}"
            : "Version 1.0";
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
