using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Fluence.Modules.SolutionView.ViewModels;

namespace Fluence.Modules.SolutionView.Views;

public partial class SolutionView : UserControl
{
    public SolutionView()
    {
        InitializeComponent();
        SolutionTree.DoubleTapped += OnTreeDoubleTapped;
        SolutionTree.AddHandler(TreeViewItem.ExpandedEvent, OnTreeItemExpanded);
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SolutionTree.SelectedItem is SolutionTreeItem item)
            item.Activate();
    }

    private static void OnTreeItemExpanded(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TreeViewItem treeItem && treeItem.DataContext is SolutionTreeItem item)
            item.IsExpanded = true;
    }
}
