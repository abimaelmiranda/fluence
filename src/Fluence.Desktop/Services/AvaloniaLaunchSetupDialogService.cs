using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.ApplicationLifetimes;
using Fluence.Core.Ports;

namespace Fluence.Desktop.Services;

public sealed class AvaloniaLaunchSetupDialogService : ILaunchSetupDialogService
{
    public Task<string?> SelectStartupProjectAsync(
        string workspaceRoot,
        IReadOnlyList<string> projectPaths,
        CancellationToken cancellationToken = default)
    {
        if (projectPaths.Count == 0)
            return Task.FromResult<string?>(null);

        var choices = projectPaths
            .Select(path => new ProjectChoice(
                Path.GetFileNameWithoutExtension(path),
                Path.GetRelativePath(workspaceRoot, path),
                path))
            .ToArray();

        var window = new Window
        {
            Title = "Select Startup Project",
            Width = 520,
            Height = 420,
            MinWidth = 420,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
        };

        var list = new ListBox
        {
            ItemsSource = choices,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var okButton = new Button
        {
            Content = "Run",
            Width = 92,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 92,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        okButton.Click += (_, _) =>
        {
            window.Close((list.SelectedItem as ProjectChoice)?.ProjectPath);
        };
        cancelButton.Click += (_, _) => window.Close(null);

        window.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(16),
            RowSpacing = 12,
            Children =
            {
                new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Startup Project",
                            FontSize = 16,
                            FontWeight = FontWeight.SemiBold,
                        },
                        new TextBlock
                        {
                            Text = "Choose the project Fluence should run for this workspace.",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = Brushes.Gray,
                        },
                    },
                },
                list.WithGridRow(1),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancelButton, okButton },
                }.WithGridRow(2),
            },
        };

        using var _ = cancellationToken.Register(() => window.Close(null));
        return ShowAsync(window, cancellationToken);
    }

    private static async Task<string?> ShowAsync(Window window, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return null;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            return await window.ShowDialog<string?>(owner);

        window.Show();
        return null;
    }

    private sealed class ProjectChoice(string name, string relativePath, string projectPath)
    {
        public string ProjectPath { get; } = projectPath;

        public override string ToString() => $"{name}  -  {relativePath}";
    }
}

internal static class GridPlacementExtensions
{
    public static T WithGridRow<T>(this T control, int row)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
