using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Fluence.Modules.Workbench.FileExplorer.ViewModels;

namespace Fluence.Modules.Workbench.FileExplorer.Converters;

public sealed class FileTreeItemTextIconConverter : IValueConverter
{
    public static readonly FileTreeItemTextIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FileTreeItem { IsDirectory: false } item)
            return string.Empty;

        return FileTreeIconCatalog.GetTextIcon(item.Path);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
