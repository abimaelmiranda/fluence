using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Models.Keybindings;
using Fluence.Core.Services;
using Fluence.Desktop.ViewModels;

namespace Fluence.Desktop.Views;

public partial class MainWindow : Window
{
    private const double TerminalResizeDragThreshold = 4;

    private bool _isTerminalResizePointerDown;
    private bool _isTerminalResizeDragging;
    private double _terminalResizeStartY;
    private double _terminalResizeStartHeight;
    private IDisposable? _keybindingSubscription;
    private MainWindowViewModel? _observedViewModel;
    private PropertyChangedEventHandler? _quickOpenPropertyChangedHandler;

    public MainWindow()
    {
        InitializeComponent();
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble);
    }

    // NOTE: Gestures containing Ctrl+Tab (or any Ctrl+<Tab> combo) are not dispatched
    // through the keybinding service. Avalonia's KeyboardNavigationHandler intercepts
    // Ctrl+Tab at the KeyboardDevice level — before routing begins — and never raises
    // the KeyDownEvent for Tab when Ctrl is held. Window.KeyBindings and InputManager.
    // PreProcess (via reflection) were both attempted without success. Until Avalonia
    // exposes a public API to suppress tab-group navigation, Ctrl+Tab cannot be
    // reliably bound to IDE commands.
    private void RebuildDynamicKeyBindings(IReadOnlyList<KeybindingDefinition> bindings)
    {
        Dispatcher.UIThread.Post(() =>
        {
            KeyBindings.Clear();
            foreach (var binding in bindings)
            {
                if (!string.Equals(binding.Scope, KeybindingScope.Global, StringComparison.Ordinal))
                    continue;
                if (string.IsNullOrWhiteSpace(binding.Key))
                    continue;
                var gesture = KeyGestureParser.TryParse(binding.Key);
                if (gesture is null)
                    continue;
                var capturedKey = binding.Key;
                KeyBindings.Add(new KeyBinding
                {
                    Gesture = gesture,
                    Command = new RelayCommand(() => _ = DispatchGlobalKeyAsync(capturedKey))
                });
            }
        });
    }

    private async Task DispatchGlobalKeyAsync(string key)
    {
        if (DataContext is MainWindowViewModel vm)
            await vm.TryHandleKeybindingAsync("global", key);
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (viewModel.IsRecordingKeybinding)
            return;

        if (MatchesQuickOpenShortcut(e))
        {
            viewModel.QuickOpen.Toggle();
            e.Handled = true;
            return;
        }

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
    }

    private static bool MatchesQuickOpenShortcut(KeyEventArgs e)
    {
        var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        return e.Key == Key.P &&
               e.KeyModifiers.HasFlag(primary) &&
               !e.KeyModifiers.HasFlag(KeyModifiers.Alt) &&
               !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
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
        if (change.Property != DataContextProperty)
            return;

        _keybindingSubscription?.Dispose();
        _keybindingSubscription = null;
        if (_observedViewModel is not null &&
            _quickOpenPropertyChangedHandler is not null)
        {
            _observedViewModel.QuickOpen.PropertyChanged -= _quickOpenPropertyChangedHandler;
        }

        _observedViewModel = null;
        _quickOpenPropertyChangedHandler = null;

        if (DataContext is not MainWindowViewModel vm)
            return;

        _observedViewModel = vm;
        _keybindingSubscription = vm.Keybindings.Watch()
            .Subscribe(new ActionObserver<IReadOnlyList<KeybindingDefinition>>(RebuildDynamicKeyBindings));
        _quickOpenPropertyChangedHandler = (_, args) =>
        {
            if (args.PropertyName == nameof(QuickOpenViewModel.IsVisible) && vm.QuickOpen.IsVisible)
                FocusQuickOpenSearchBox();
        };
        vm.QuickOpen.PropertyChanged += _quickOpenPropertyChangedHandler;

        if (vm.QuickOpen.IsVisible)
            FocusQuickOpenSearchBox();
    }

    private void FocusQuickOpenSearchBox()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (QuickOpenSearchBox is null || !QuickOpenSearchBox.IsVisible)
                return;

            QuickOpenSearchBox.Focus();
            QuickOpenSearchBox.SelectAll();
        }, DispatcherPriority.Loaded);
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
