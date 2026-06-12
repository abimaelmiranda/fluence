using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Fluence.Modules.FileExplorer.ViewModels;

namespace Fluence.Modules.FileExplorer.Converters;

public sealed class FileTreeItemIconConverter : IValueConverter
{
    public static readonly FileTreeItemIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FileTreeItem item)
            return FileTreeIconCatalog.FileIcon;

        return item.IsDirectory
            ? FileTreeIconCatalog.FolderIcon
            : FileTreeIconCatalog.FileIcon;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
