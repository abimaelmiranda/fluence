using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Fluence.Shared.MaterialIcons;
using Fluence.Modules.FileExplorer.ViewModels;

namespace Fluence.Modules.FileExplorer.Converters;

public sealed class FileTreeItemIconConverter : IValueConverter
{
    public static readonly FileTreeItemIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FileTreeItem item)
            return MaterialIconTheme.Instance.GetIconById("file")!;

        return item.IsDirectory
            ? MaterialIconTheme.Instance.GetFolderIcon(item.Name)!
            : MaterialIconTheme.Instance.GetFileIcon(item.Path)!;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
