using System;
using Avalonia.Controls;
using Avalonia.Input;
using Fluence.Desktop.ViewModels;

namespace Fluence.Desktop.Views;

public partial class MainWindow : Window
{
    private const double TerminalResizeDragThreshold = 4;

    private bool _isTerminalResizePointerDown;
    private bool _isTerminalResizeDragging;
    private double _terminalResizeStartY;
    private double _terminalResizeStartHeight;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnTerminalResizeHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        _isTerminalResizePointerDown = true;
        _isTerminalResizeDragging = false;
        _terminalResizeStartY = point.Position.Y;
        _terminalResizeStartHeight = viewModel.TerminalHeight;
        e.Pointer.Capture(TerminalResizeHandle);
        e.Handled = true;
    }

    private void OnTerminalResizeHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isTerminalResizePointerDown || DataContext is not MainWindowViewModel viewModel)
            return;

        var currentY = e.GetPosition(this).Y;
        var delta = _terminalResizeStartY - currentY;
        if (!_isTerminalResizeDragging && Math.Abs(delta) < TerminalResizeDragThreshold)
            return;

        _isTerminalResizeDragging = true;
        viewModel.IsTerminalExpanded = true;
        viewModel.SetTerminalHeight(_terminalResizeStartHeight + delta, Bounds.Height / 2);
        e.Handled = true;
    }

    private void OnTerminalResizeHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isTerminalResizePointerDown)
            return;

        _isTerminalResizePointerDown = false;
        _isTerminalResizeDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnTerminalResizeHandlePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isTerminalResizePointerDown = false;
        _isTerminalResizeDragging = false;
    }

    private void OnTerminalToggleHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ToggleTerminalCommand.Execute(null);
            e.Handled = true;
        }
    }
}
