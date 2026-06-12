using Avalonia.Controls;
using Avalonia.Input;
using Fluence.Modules.SolutionView.ViewModels;

namespace Fluence.Modules.SolutionView.Views;

public partial class SolutionView : UserControl
{
    public SolutionView()
    {
        InitializeComponent();
        SolutionTree.DoubleTapped += OnTreeDoubleTapped;
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SolutionTree.SelectedItem is SolutionTreeItem item)
            item.Activate();
    }
}
