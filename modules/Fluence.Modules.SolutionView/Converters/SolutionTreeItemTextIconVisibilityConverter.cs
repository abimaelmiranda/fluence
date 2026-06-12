using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Fluence.Modules.SolutionView.Converters;

public sealed class SolutionTreeItemTextIconVisibilityConverter : IValueConverter
{
    public static readonly SolutionTreeItemTextIconVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(SolutionTreeItemTextIconConverter.Instance.Convert(value, typeof(string), parameter, culture) as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
