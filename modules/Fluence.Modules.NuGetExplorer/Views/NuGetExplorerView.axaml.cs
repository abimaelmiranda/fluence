using Avalonia.Controls;
using Avalonia.Input;
using Fluence.Modules.NuGetExplorer.ViewModels;

namespace Fluence.Modules.NuGetExplorer.Views;

public partial class NuGetExplorerView : UserControl
{
    public NuGetExplorerView()
    {
        InitializeComponent();
        SearchTextBox.KeyDown += OnSearchTextBoxKeyDown;
    }

    private void OnSearchTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not NuGetExplorerViewModel viewModel)
        {
            return;
        }

        if (viewModel.SearchCommand.CanExecute(null))
        {
            viewModel.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
