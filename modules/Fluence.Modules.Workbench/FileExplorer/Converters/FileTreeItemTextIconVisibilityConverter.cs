using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Fluence.Modules.Workbench.FileExplorer.Converters;

public sealed class FileTreeItemTextIconVisibilityConverter : IValueConverter
{
    public static readonly FileTreeItemTextIconVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(FileTreeItemTextIconConverter.Instance.Convert(value, typeof(string), parameter, culture) as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
