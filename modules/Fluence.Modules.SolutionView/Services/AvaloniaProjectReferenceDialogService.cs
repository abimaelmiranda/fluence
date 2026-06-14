using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Fluence.Modules.SolutionView.Abstractions;
using Fluence.Modules.SolutionView.Models;

namespace Fluence.Modules.SolutionView.Services;

public sealed class AvaloniaProjectReferenceDialogService : IProjectReferenceDialogService
{
    public Task<IReadOnlyList<string>> ShowAddReferenceDialogAsync(
        IReadOnlyList<ProjectReferenceCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var selections = candidates
            .Select(candidate => new ReferenceSelection(candidate))
            .ToArray();

        var window = CreateWindow("Add Reference", 520, 420);
        var list = new ListBox
        {
            ItemsSource = selections,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ReferenceSelection>((selection, _) =>
        {
            var checkBox = new CheckBox
            {
                Content = selection?.Candidate.Name,
                IsChecked = selection?.IsSelected,
                Margin = new Thickness(8, 4),
            };

            if (selection is not null)
                checkBox.Click += (_, _) => selection.IsSelected = checkBox.IsChecked == true;

            return checkBox;
        });

        var okButton = new Button { Content = "OK", MinWidth = 84 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 84 };
        okButton.Click += (_, _) => window.Close(selections.Where(selection => selection.IsSelected)
            .Select(selection => selection.Candidate.ProjectPath)
            .ToArray());
        cancelButton.Click += (_, _) => window.Close(System.Array.Empty<string>());

        window.Content = CreateDialogLayout(list, okButton, cancelButton);
        return ShowDialogAsync<IReadOnlyList<string>>(window, System.Array.Empty<string>());
    }

    public Task<bool> ConfirmRemoveProjectReferenceAsync(
        string referenceName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var window = CreateWindow("Remove Reference", 420, 160);
        var message = new TextBlock
        {
            Text = $"Remove project reference '{referenceName}'?",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16),
        };

        var removeButton = new Button { Content = "Remove", MinWidth = 84 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 84 };
        removeButton.Click += (_, _) => window.Close(true);
        cancelButton.Click += (_, _) => window.Close(false);

        window.Content = CreateDialogLayout(message, removeButton, cancelButton);
        return ShowDialogAsync(window, false);
    }

    private static Window CreateWindow(string title, double width, double height)
    {
        return new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = 360,
            MinHeight = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
    }

    private static Control CreateDialogLayout(Control content, Button primaryButton, Button cancelButton)
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { primaryButton, cancelButton },
        };

        Grid.SetRow(content, 0);
        Grid.SetRow(buttons, 1);

        return new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(16),
            Children =
            {
                content,
                buttons,
            },
        };
    }

    private static async Task<T> ShowDialogAsync<T>(Window window, T fallback)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            return await window.ShowDialog<T>(owner);

        window.Show();
        return fallback;
    }

    private sealed class ReferenceSelection(ProjectReferenceCandidate candidate)
    {
        public ProjectReferenceCandidate Candidate { get; } = candidate;

        public bool IsSelected { get; set; } = candidate.IsReferenced;
    }
}
