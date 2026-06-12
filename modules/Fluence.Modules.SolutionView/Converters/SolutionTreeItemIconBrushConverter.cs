using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Fluence.Modules.SolutionView.ViewModels;

namespace Fluence.Modules.SolutionView.Converters;

public sealed class SolutionTreeItemIconBrushConverter : IValueConverter
{
    public static readonly SolutionTreeItemIconBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SolutionTreeItem item)
            return SolutionTreeIconCatalog.FileBrush;

        return item.Kind switch
        {
            SolutionTreeNodeKind.Solution => SolutionTreeIconCatalog.SolutionBrush,
            SolutionTreeNodeKind.SolutionFolder or SolutionTreeNodeKind.Folder => SolutionTreeIconCatalog.FolderBrush,
            SolutionTreeNodeKind.Project => SolutionTreeIconCatalog.ProjectBrush,
            SolutionTreeNodeKind.Dependencies or SolutionTreeNodeKind.DependencyGroup => SolutionTreeIconCatalog.DependencyBrush,
            SolutionTreeNodeKind.ProjectReference => SolutionTreeIconCatalog.ReferenceBrush,
            SolutionTreeNodeKind.PackageReference => SolutionTreeIconCatalog.PackageBrush,
            SolutionTreeNodeKind.File => GetFileBrush(item),
            _ => SolutionTreeIconCatalog.FileBrush,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static IBrush GetFileBrush(SolutionTreeItem item)
    {
        var path = item.Path ?? item.Name;
        return SolutionTreeIconCatalog.GetFileBrush(path);
    }
}
