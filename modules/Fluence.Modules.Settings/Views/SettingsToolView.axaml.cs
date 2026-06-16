using Avalonia.Controls;
using Avalonia.Input;
using Fluence.Modules.Settings.Services;
using Fluence.Modules.Settings.ViewModels;

namespace Fluence.Modules.Settings.Views;

public partial class SettingsToolView : UserControl
{
    public SettingsToolView()
    {
        InitializeComponent();
    }

    private void OnKeybindingTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: KeybindingRowViewModel row } || !row.IsRecording)
            return;

        CaptureKeybinding(row, e);
    }

    private void OnKeybindingRecordKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Button { DataContext: KeybindingRowViewModel row } || !row.IsRecording)
            return;

        CaptureKeybinding(row, e);
    }

    private static void CaptureKeybinding(KeybindingRowViewModel row, KeyEventArgs e)
    {
        row.HandleCapture(KeyGestureFormatter.FromEvent(e), e.Key == Key.Escape);
        e.Handled = true;
    }
}
