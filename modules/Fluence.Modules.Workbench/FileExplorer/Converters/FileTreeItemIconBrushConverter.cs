using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Fluence.Modules.Workbench.FileExplorer.ViewModels;

namespace Fluence.Modules.Workbench.FileExplorer.Converters;

public sealed class FileTreeItemIconBrushConverter : IValueConverter
{
    public static readonly FileTreeItemIconBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not FileTreeItem item)
            return FileTreeIconCatalog.FileBrush;

        return item.IsDirectory
            ? FileTreeIconCatalog.FolderBrush
            : FileTreeIconCatalog.GetFileBrush(item.Path);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
