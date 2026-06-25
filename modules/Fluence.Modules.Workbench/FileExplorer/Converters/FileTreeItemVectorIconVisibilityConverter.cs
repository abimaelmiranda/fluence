using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Fluence.Modules.Workbench.FileExplorer.Converters;

public sealed class FileTreeItemVectorIconVisibilityConverter : IValueConverter
{
    public static readonly FileTreeItemVectorIconVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !(FileTreeItemTextIconVisibilityConverter.Instance.Convert(value, typeof(bool), parameter, culture) as bool? ?? false);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
