using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Fluence.Desktop.Converters;

public sealed class ActivityBarBgConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var activeTab = value as string;
        var targetTab = parameter as string;
        return activeTab == targetTab
            ? FindBrush("FluenceBrushActivityBarActiveBackground", "#602676")
            : Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static IBrush FindBrush(string key, string fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is IBrush brush)
            return brush;

        return new SolidColorBrush(Color.Parse(fallback));
    }
}
