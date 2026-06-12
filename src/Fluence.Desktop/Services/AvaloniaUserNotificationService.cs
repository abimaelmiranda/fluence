using System;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;

namespace Fluence.Desktop.Services;

public sealed class AvaloniaUserNotificationService : IUserNotificationService
{
    private WindowNotificationManager? _manager;

    public void Attach(Window window)
    {
        _manager = new WindowNotificationManager(window)
        {
            Position = NotificationPosition.BottomRight,
            MaxItems = 3,
        };
    }

    public void ShowWarning(string title, string message)
    {
        Show(title, message, NotificationType.Warning);
    }

    public void ShowError(string title, string message)
    {
        Show(title, message, NotificationType.Error);
    }

    private void Show(string title, string message, NotificationType type)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var content = new StackPanel
            {
                MaxWidth = 300,
                Spacing = 2,
                Margin = new Avalonia.Thickness(0),
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold,
                        FontSize = 12,
                        TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                        TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                    },
                    new TextBlock
                    {
                        Text = message,
                        FontSize = 12,
                        MaxWidth = 280,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                },
            };

            _manager?.Show(content, type, TimeSpan.FromSeconds(3), null, null, Array.Empty<string>());
        });
    }
}
