using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Fluence.Modules.SolutionView.Views.Converters;

public sealed class SolutionTreeItemVectorIconVisibilityConverter : IValueConverter
{
    public static readonly SolutionTreeItemVectorIconVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !(SolutionTreeItemTextIconVisibilityConverter.Instance.Convert(value, typeof(bool), parameter, culture) as bool? ?? false);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
