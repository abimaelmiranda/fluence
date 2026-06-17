using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Fluence.Modules.SolutionView.Abstractions;
using Fluence.Modules.SolutionView.Models;
using Fluence.Modules.SolutionView.Models.Enums;

namespace Fluence.Modules.SolutionView.Services;

public sealed class AvaloniaSolutionFileCreationDialogService : ISolutionFileCreationDialogService
{
    public Task<SolutionFileCreationRequest?> ShowCreateFileDialogAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var window = CreateWindow();

        var nameLabel = new TextBlock
        {
            Text = "Name",
            Margin = new Thickness(0, 0, 0, 8),
        };

        var nameBox = new TextBox
        {
            MinWidth = 320,
        };

        var kindLabel = new TextBlock
        {
            Text = "Type",
            Margin = new Thickness(0, 12, 0, 8),
        };

        var kindBox = new ComboBox
        {
            ItemsSource = Enum.GetValues<SolutionFileKind>().ToArray(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var createButton = new Button
        {
            Content = "Create",
            MinWidth = 84,
            IsDefault = true,
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            IsCancel = true,
        };

        createButton.Click += (_, _) =>
        {
            var name = string.IsNullOrWhiteSpace(nameBox.Text) ? null : nameBox.Text.Trim();
            var kind = kindBox.SelectedItem is SolutionFileKind selected ? selected : SolutionFileKind.Class;
            window.Close(name is null ? null : new SolutionFileCreationRequest(name, kind));
        };

        cancelButton.Click += (_, _) => window.Close(null);

        var layout = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 0,
            Children =
            {
                nameLabel,
                nameBox,
                kindLabel,
                kindBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(0, 16, 0, 0),
                    Children = { cancelButton, createButton },
                },
            },
        };

        window.Content = layout;
        return ShowDialogAsync(window);
    }

    private static Window CreateWindow()
    {
        return new Window
        {
            Title = "Create C# File",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
    }

    private static async Task<SolutionFileCreationRequest?> ShowDialogAsync(Window window)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return await window.ShowDialog<SolutionFileCreationRequest?>(owner);
        }

        window.Show();
        return null;
    }
}
