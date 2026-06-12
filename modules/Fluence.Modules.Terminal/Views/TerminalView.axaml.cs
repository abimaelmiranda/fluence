using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Fluence.Modules.Terminal.ViewModels;

namespace Fluence.Modules.Terminal.Views;

public partial class TerminalView : UserControl
{
    private TerminalViewModel? _viewModel;

    public TerminalView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        CommandInput.KeyDown += OnCommandInputKeyDown;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.Lines.CollectionChanged -= OnLinesChanged;

        _viewModel = DataContext as TerminalViewModel;

        if (_viewModel is not null)
            _viewModel.Lines.CollectionChanged += OnLinesChanged;
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(
            () => TerminalScrollViewer.Offset = new Vector(
                TerminalScrollViewer.Offset.X,
                TerminalScrollViewer.Extent.Height),
            DispatcherPriority.Background);
    }

    private void OnCommandInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _viewModel is null)
            return;

        e.Handled = true;
        _viewModel.CurrentCommand = CommandInput.Text ?? string.Empty;
        if (_viewModel.ExecuteCurrentCommandCommand.CanExecute(null))
            _viewModel.ExecuteCurrentCommandCommand.Execute(null);
    }
}
