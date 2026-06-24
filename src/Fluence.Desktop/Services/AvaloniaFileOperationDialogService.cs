using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.File;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Services.File;

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

    public Task<string?> PromptForNameAsync(
        string title,
        string label,
        string? initialValue = null,
        CancellationToken cancellationToken = default)
    {
        var window = CreateWindow(title, 420);

        var prompt = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 13,
            Foreground = Brushes.White,
        };

        var textBox = new TextBox
        {
            Text = initialValue ?? string.Empty,
            MinWidth = 320,
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 78,
            IsDefault = true,
            Background = Brushes.Transparent,
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 78,
            IsCancel = true,
            Background = Brushes.Transparent,
        };

        var layout = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                prompt,
                textBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancelButton, okButton },
                },
            },
        };

        window.Content = layout;
        window.Opened += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                textBox.Focus();
                textBox.SelectAll();
            }, DispatcherPriority.Loaded);
        };
        okButton.Click += (_, _) => window.Close(string.IsNullOrWhiteSpace(textBox.Text) ? null : textBox.Text.Trim());
        cancelButton.Click += (_, _) => window.Close(null);

        return ShowAsync(window, (string?)null, cancellationToken);
    }

    private static Window CreateWindow()
        => CreateWindow("Delete", 360);

    private static Window CreateWindow(string title, double width)
    {
        return new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
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

        return ShowAsync(window, false, cancellationToken);
    }

    private static async Task<T> ShowAsync<T>(Window window, T fallback, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var _ = cancellationToken.Register(() => window.Close(fallback));

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return await window.ShowDialog<T>(owner);
        }

        window.Show();
        return fallback;
    }
}
