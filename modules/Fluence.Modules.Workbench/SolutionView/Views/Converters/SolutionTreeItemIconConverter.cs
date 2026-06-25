using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Fluence.Shared.MaterialIcons;
using Fluence.Modules.Workbench.SolutionView.Models;
using Fluence.Modules.Workbench.SolutionView.ViewModels;
using Fluence.Modules.Workbench.SolutionView.Models.Enums;

namespace Fluence.Modules.Workbench.SolutionView.Views.Converters;

public sealed class SolutionTreeItemIconConverter : IValueConverter
{
    public static readonly SolutionTreeItemIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SolutionTreeItem item)
            return MaterialIconTheme.Instance.GetIconById("file")!;

        return item.Kind switch
        {
            SolutionTreeNodeKind.Solution => MaterialIconTheme.Instance.GetIconById("visualstudio")!,
            SolutionTreeNodeKind.SolutionFolder or SolutionTreeNodeKind.Folder =>
                MaterialIconTheme.Instance.GetFolderIcon(item.Name) ??
                MaterialIconTheme.Instance.GetIconById("folder") ??
                MaterialIconTheme.Instance.GetIconById("file")!,
            SolutionTreeNodeKind.Project => MaterialIconTheme.Instance.GetIconById("visualstudio")!,
            SolutionTreeNodeKind.Dependencies or SolutionTreeNodeKind.DependencyGroup => MaterialIconTheme.Instance.GetIconById("dependencies-update")!,
            SolutionTreeNodeKind.ProjectReference => MaterialIconTheme.Instance.GetFileIcon(item.ReferencedProjectPath ?? item.Name)!,
            SolutionTreeNodeKind.PackageReference => MaterialIconTheme.Instance.GetIconById("nuget")!,
            SolutionTreeNodeKind.File => MaterialIconTheme.Instance.GetFileIcon(item.Path ?? item.Name)!,
            _ => MaterialIconTheme.Instance.GetIconById("file")!,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

}
