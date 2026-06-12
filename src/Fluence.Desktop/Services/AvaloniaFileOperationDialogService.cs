using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Fluence.Core.Ports;

namespace Fluence.Desktop.Services;

public sealed class AvaloniaFileOperationDialogService : IFileOperationDialogService
{
    public Task<bool> ConfirmDeleteAsync(string path, bool isDirectory, CancellationToken cancellationToken = default)
    {
        var window = CreateWindow();
        var message = isDirectory
            ? $"Delete folder \"{System.IO.Path.GetFileName(path)}\"?"
            : $"Delete file \"{System.IO.Path.GetFileName(path)}\"?";

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            FontSize = 13,
            Foreground = Brushes.White,
        };

        var delete = new Button
        {
            Content = "Delete",
            Width = 78,
            IsDefault = true,
            Background = Brushes.Transparent,
        };

        var cancel = new Button
        {
            Content = "Cancel",
            Width = 78,
            IsCancel = true,
            Background = Brushes.Transparent,
        };

        var result = ShowDialogAsync(window, text, delete, cancel, cancellationToken);
        return result;
    }

    private static Window CreateWindow()
    {
        return new Window
        {
            Title = "Delete",
            Width = 360,
            Height = 150,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#171B20")),
        };
    }

    private static Task<bool> ShowDialogAsync(Window window, Control content, Button primaryButton, Button cancelButton, CancellationToken cancellationToken)
    {
        var layout = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 0,
            Children =
            {
                content,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancelButton, primaryButton },
                },
            },
        };

        window.Content = layout;
        primaryButton.Click += (_, _) => window.Close(true);
        cancelButton.Click += (_, _) => window.Close(false);

        return ShowAsync(window, cancellationToken);
    }

    private static async Task<bool> ShowAsync(Window window, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var _ = cancellationToken.Register(() => window.Close(false));

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return await window.ShowDialog<bool>(owner);
        }

        window.Show();
        return false;
    }
}
