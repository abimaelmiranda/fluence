using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Fluence.Modules.SourceControl.Abstractions;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.Services;

public sealed class AvaloniaSourceControlDialogService : ISourceControlDialogService
{
    public Task<bool> ConfirmRevertFileAsync(GitFileChange change, CancellationToken cancellationToken = default)
    {
        var window = new Window
        {
            Title = "Discard changes?",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#171B20")),
        };

        var message = change.DeletesUntrackedFile
            ? $"Delete untracked file \"{change.FileName}\"? This action cannot be undone."
            : $"Discard all changes in \"{change.FileName}\" and restore it to HEAD? This action cannot be undone.";

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = Brushes.White,
        };

        var discardButton = new Button
        {
            Content = change.DeletesUntrackedFile ? "Delete" : "Discard",
            Width = 88,
            IsDefault = true,
            Background = Brushes.Transparent,
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 88,
            IsCancel = true,
            Background = Brushes.Transparent,
        };

        window.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 0,
            Children =
            {
                text,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancelButton, discardButton },
                },
            },
        };

        discardButton.Click += (_, _) => window.Close(true);
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
