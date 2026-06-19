using System;
using System.Globalization;
using System.Reflection;

namespace Fluence.Modules.Settings.ViewModels;

internal static class SettingsValueConverter
{
    public static object? Convert(PropertyInfo property, SettingsPropertyViewModel item)
    {
        try
        {
            if (property.PropertyType == typeof(bool))
                return item.BoolValue;

            if (property.PropertyType == typeof(string))
            {
                if (item.IsThemeSelector)
                    return item.ThemeReference;

                if (item.IsLanguageSelector)
                    return item.SelectedLanguage?.Code;

                if (string.IsNullOrWhiteSpace(item.TextValue))
                    return null;

                return item.TextValue.Trim();
            }

            if (property.PropertyType == typeof(string[]))
                return item.GetStringArrayValues();

            if (property.PropertyType.IsEnum)
                return Enum.Parse(property.PropertyType, item.SelectedEnumValue, ignoreCase: true);

            return System.Convert.ChangeType(item.TextValue, property.PropertyType, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }
}
