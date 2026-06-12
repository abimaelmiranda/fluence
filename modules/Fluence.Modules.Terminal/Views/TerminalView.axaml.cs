using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Fluence.Modules.Terminal.ViewModels;

namespace Fluence.Modules.Terminal.Views;

public partial class TerminalView : UserControl
{
    private TerminalViewModel? _viewModel;
    private bool _shellStarted;

    public TerminalView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        _viewModel = DataContext as TerminalViewModel;
        if (_viewModel is null)
            return;

        TerminalControl.TerminalTextInput = text => _ = _viewModel.SendInputAsync(text);
        TerminalControl.Resized = (cols, rows) => _ = _viewModel.ResizeAsync(cols, rows);

        if (IsLoaded && IsVisible)
            TryStartShell();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (IsVisible)
            TryStartShell();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Terminal panel starts collapsed. Start the shell the first time it becomes visible.
        if (change.Property == IsVisibleProperty && change.NewValue is true)
        {
            TryStartShell();
            TerminalControl.RequestInitialResize();
        }
    }

    private void TryStartShell()
    {
        if (_shellStarted || _viewModel is null)
            return;

        _shellStarted = true;
        TerminalControl.RequestInitialResize();
        _ = StartShellSafeAsync();
    }

    private async System.Threading.Tasks.Task StartShellSafeAsync()
    {
        try
        {
            await _viewModel!.StartShellAsync();
        }
        catch (Exception ex)
        {
            _viewModel!.WriteError($"\r\n[Terminal error: {ex.Message}]\r\n");
        }
    }
}
