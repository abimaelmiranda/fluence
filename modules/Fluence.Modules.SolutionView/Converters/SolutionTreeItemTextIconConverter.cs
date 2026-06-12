using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Fluence.Modules.SolutionView.ViewModels;

namespace Fluence.Modules.SolutionView.Converters;

public sealed class SolutionTreeItemTextIconConverter : IValueConverter
{
    public static readonly SolutionTreeItemTextIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SolutionTreeItem item)
            return string.Empty;

        if (item.Kind == SolutionTreeNodeKind.Solution)
            return SolutionTreeIconCatalog.SolutionTextIcon;

        if (item.Kind == SolutionTreeNodeKind.Project)
            return SolutionTreeIconCatalog.ProjectTextIcon;

        if (item.Kind != SolutionTreeNodeKind.File)
            return string.Empty;

        var path = item.Path ?? item.Name;
        return SolutionTreeIconCatalog.GetTextIcon(path);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
