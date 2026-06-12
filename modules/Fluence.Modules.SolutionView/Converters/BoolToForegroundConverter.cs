using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Fluence.Modules.SolutionView.Converters;

public sealed class BoolToForegroundConverter(IBrush trueBrush, IBrush falseBrush) : IValueConverter
{
    public static readonly BoolToForegroundConverter ActiveFile = new(
        new SolidColorBrush(Color.Parse("#4FA3FF")),
        new SolidColorBrush(Color.Parse("#F2F5F8")));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? trueBrush : falseBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
