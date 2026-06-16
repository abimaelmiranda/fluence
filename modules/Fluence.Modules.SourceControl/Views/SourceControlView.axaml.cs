using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Fluence.Modules.SourceControl.ViewModels;

namespace Fluence.Modules.SourceControl.Views;

public partial class SourceControlView : UserControl
{
    public SourceControlView()
    {
        InitializeComponent();
    }

    private static void OnChangeRowTapped(object? sender, TappedEventArgs e)
    {
        if (TappedInsideButton(e.Source))
        {
            e.Handled = true;
            return;
        }

        if (sender is Control { DataContext: GitFileChangeViewModel item }
            && item.OpenFileCommand.CanExecute(null))
        {
            item.OpenFileCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static bool TappedInsideButton(object? source)
    {
        if (source is not Visual visual)
            return false;

        if (visual is Button)
            return true;

        foreach (var ancestor in visual.GetVisualAncestors())
        {
            if (ancestor is Button)
                return true;
        }

        return false;
    }
}
