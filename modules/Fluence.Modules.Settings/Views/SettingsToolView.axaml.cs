using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Fluence.Modules.Settings.Services;
using Fluence.Modules.Settings.ViewModels;

namespace Fluence.Modules.Settings.Views;

public partial class SettingsToolView : UserControl
{
    public SettingsToolView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnSettingsPreviewKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnSettingsPreviewKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        DetachedFromVisualTree += (_, _) =>
        {
            if (DataContext is SettingsToolViewModel viewModel)
                viewModel.CancelKeybindingCapture();
        };
    }

    private void OnSuppressionsScrollWheel(object? sender, PointerWheelEventArgs e) => e.Handled = true;

    private void OnSettingsPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (VisualRoot is null)
            return;

        if (DataContext is not SettingsToolViewModel viewModel || !viewModel.IsRecordingKeybinding)
            return;

        if (viewModel.TryCaptureKeybinding(
                KeyGestureFormatter.FromEvent(e),
                KeyGestureFormatter.IsCancelCapture(e)))
            e.Handled = true;
    }

    private void OnSettingsPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (VisualRoot is null)
            return;

        if (DataContext is not SettingsToolViewModel viewModel || !viewModel.IsRecordingKeybinding)
            return;

        if (viewModel.TryCaptureKeybinding(
                KeyGestureFormatter.FromEvent(e),
                KeyGestureFormatter.IsCancelCapture(e)))
            e.Handled = true;
    }
}
