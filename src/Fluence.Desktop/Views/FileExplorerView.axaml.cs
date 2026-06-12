using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Fluence.Desktop.ViewModels;

namespace Fluence.Desktop.Views;

public partial class FileExplorerView : UserControl
{
    public FileExplorerView()
    {
        InitializeComponent();
        FileTree.DoubleTapped += OnTreeDoubleTapped;
        FileTree.AddHandler(TreeViewItem.ExpandedEvent, OnTreeItemExpanded);
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FileTree.SelectedItem is FileTreeItem item)
        {
            item.Activate();
        }
    }

    private static void OnTreeItemExpanded(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TreeViewItem treeItem && treeItem.DataContext is FileTreeItem fileItem)
        {
            fileItem.IsExpanded = true;
        }
    }
}
