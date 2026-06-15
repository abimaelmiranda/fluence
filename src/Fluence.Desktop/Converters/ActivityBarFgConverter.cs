using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Fluence.Desktop.Converters;

public sealed class ActivityBarFgConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var activeTab = value as string;
        var targetTab = parameter as string;
        return activeTab == targetTab
            ? new SolidColorBrush(Color.Parse("#F2F5F8"))
            : new SolidColorBrush(Color.Parse("#AAB4BF"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
