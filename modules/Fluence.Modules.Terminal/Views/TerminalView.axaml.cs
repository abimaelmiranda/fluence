using System;
using System.Threading.Tasks;
using Avalonia.Controls;
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
        _viewModel.BufferRefreshed += (_, _) => TerminalControl.RequestRedraw();

        // Start the shell the first time ArrangeOverride gives us real bounds.
        // This avoids starting the shell while the terminal panel is collapsed (Bounds={0,0,0,0}),
        // which would cause the shell prompt to arrive before the first resize and get lost.
        TerminalControl.Resized = (cols, rows) =>
        {
            if (!_shellStarted)
            {
                _shellStarted = true;
                _ = InitAndStartShellAsync(cols, rows);
            }
            else
            {
                _ = _viewModel.ResizeAsync(cols, rows);
            }
        };

        // If ArrangeOverride already ran before DataContext was set, _lastCols/_lastRows are
        // non-zero and a new Arrange won't fire Resized again. RequestInitialResize resets
        // them so the next Arrange triggers the shell start with the correct dimensions.
        TerminalControl.RequestInitialResize();
    }

    private async Task InitAndStartShellAsync(int cols, int rows)
    {
        if (_viewModel is null)
            return;

        // Resize xterm to the actual terminal dimensions before spawning the PTY,
        // so the shell sees the correct size from the very first byte it outputs.
        await _viewModel.ResizeAsync(cols, rows);

        try
        {
            await _viewModel.StartShellAsync();
        }
        catch (Exception ex)
        {
            _viewModel.WriteError($"\r\n[Terminal error: {ex.Message}]\r\n");
        }
    }
}
