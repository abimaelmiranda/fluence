using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;

namespace Fluence.Desktop.Views;

public partial class CrashDialog : Window
{
    public CrashDialog(string stackTrace)
    {
        InitializeComponent();
        StackTraceTextBox.Text = stackTrace;
    }

    private void OnQuitClicked(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown(1);
            return;
        }

        Environment.Exit(1);
    }
}
