using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Fluence.Modules.Workbench.SolutionView.Models;
using Fluence.Modules.Workbench.SolutionView.Models.Enums;
using Fluence.Modules.Workbench.SolutionView.ViewModels;

namespace Fluence.Modules.Workbench.SolutionView.Views.Converters;

public sealed class SolutionTreeItemTextIconConverter : IValueConverter
{
    public static readonly SolutionTreeItemTextIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SolutionTreeItem item)
            return string.Empty;

        if (item.Kind is SolutionTreeNodeKind.Solution or SolutionTreeNodeKind.Project)
            return string.Empty;

        if (item.Kind != SolutionTreeNodeKind.File)
            return string.Empty;

        var path = item.Path ?? item.Name;
        return SolutionTreeIconCatalog.GetTextIcon(path);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
