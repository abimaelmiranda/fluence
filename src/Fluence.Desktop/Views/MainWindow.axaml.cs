using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Fluence.Modules.Settings.Services;
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
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble);
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (viewModel.IsRecordingKeybinding)
            return;

        if (viewModel.QuickOpen.IsVisible)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    viewModel.QuickOpen.Close();
                    e.Handled = true;
                    return;
                case Key.Enter:
                    viewModel.QuickOpen.Confirm();
                    e.Handled = true;
                    return;
                case Key.Down:
                    MoveQuickOpenSelection(1);
                    e.Handled = true;
                    return;
                case Key.Up:
                    MoveQuickOpenSelection(-1);
                    e.Handled = true;
                    return;
            }
        }

        var gesture = KeyGestureFormatter.FromEvent(e);
        if (string.IsNullOrWhiteSpace(gesture))
            return;

        if (await viewModel.TryHandleKeybindingAsync("global", gesture))
        {
            e.Handled = true;
            if (viewModel.QuickOpen.IsVisible)
                QuickOpenSearchBox?.Focus();
        }
    }

    private void MoveQuickOpenSelection(int delta)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var results = vm.QuickOpen.Results;
        if (results.Count == 0)
            return;

        var current = results.IndexOf(vm.QuickOpen.SelectedResult!);
        var next = Math.Clamp(current + delta, 0, results.Count - 1);
        vm.QuickOpen.SelectedResult = results[next];
    }

    private void OnQuickOpenBackdropPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Source != sender)
            return;

        if (DataContext is MainWindowViewModel vm)
        {
            vm.QuickOpen.Close();
            e.Handled = true;
        }
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty && DataContext is MainWindowViewModel vm)
        {
            vm.QuickOpen.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(QuickOpenViewModel.IsVisible) && vm.QuickOpen.IsVisible)
                    QuickOpenSearchBox?.Focus();
            };
        }
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
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.PersistShellLayout();
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
            viewModel.ToggleBottomBarCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnBottomBarTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Border { DataContext: BottomBarTabViewModel tab })
            return;

        if (DataContext is MainWindowViewModel viewModel && !viewModel.IsBottomBarExpanded)
            viewModel.IsBottomBarExpanded = true;

        tab.SelectCommand.Execute(tab.Id);
        e.Handled = true;
    }
}
