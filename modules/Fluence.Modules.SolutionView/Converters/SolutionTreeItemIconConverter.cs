using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Fluence.Modules.SolutionView.ViewModels;

namespace Fluence.Modules.SolutionView.Converters;

public sealed class SolutionTreeItemIconConverter : IValueConverter
{
    public static readonly SolutionTreeItemIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SolutionTreeItem item)
            return SolutionTreeIconCatalog.FileIcon;

        return item.Kind switch
        {
            SolutionTreeNodeKind.Solution => SolutionTreeIconCatalog.SolutionIcon,
            SolutionTreeNodeKind.SolutionFolder or SolutionTreeNodeKind.Folder => SolutionTreeIconCatalog.FolderIcon,
            SolutionTreeNodeKind.Project => SolutionTreeIconCatalog.ProjectIcon,
            SolutionTreeNodeKind.Dependencies or SolutionTreeNodeKind.DependencyGroup => SolutionTreeIconCatalog.DependenciesIcon,
            SolutionTreeNodeKind.ProjectReference => SolutionTreeIconCatalog.ReferenceIcon,
            SolutionTreeNodeKind.PackageReference => SolutionTreeIconCatalog.PackageIcon,
            _ => SolutionTreeIconCatalog.FileIcon,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

}
