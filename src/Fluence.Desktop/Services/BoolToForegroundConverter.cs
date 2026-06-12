using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Fluence.Desktop.Services;

public sealed class BoolToForegroundConverter(IBrush trueBrush, IBrush falseBrush) : IValueConverter
{
    public static readonly BoolToForegroundConverter ActiveFile = new(
        new SolidColorBrush(Color.Parse("#4FA3FF")),
        new SolidColorBrush(Color.Parse("#F2F5F8")));

    public static readonly BoolToForegroundConverter TerminalError = new(
        new SolidColorBrush(Color.Parse("#FF6B6B")),
        new SolidColorBrush(Color.Parse("#AAB4BF")));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? trueBrush : falseBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
