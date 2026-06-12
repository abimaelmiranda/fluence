using System;
using System.IO;
using Avalonia.Controls;
using Fluence.Modules.Terminal.ViewModels;

namespace Fluence.Modules.Terminal.Views;

public partial class TerminalView : UserControl
{
    private TerminalViewModel? _viewModel;

    public TerminalView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel = DataContext as TerminalViewModel;
        if (_viewModel is null)
            return;

        TerminalControl.TerminalTextInput = text => _ = _viewModel.SendInputAsync(text);
        TerminalControl.Resized = (cols, rows) => _ = _viewModel.ResizeAsync(cols, rows);

        _ = _viewModel.StartShellAsync(GetWorkingDirectory());
        TerminalControl.RequestInitialResize();
    }

    private string? GetWorkingDirectory()
    {
        // Prefer workspace folder; fall back to home
        return null; // TerminalViewModel.StartShellAsync resolves via IWorkspaceContext
    }
}
