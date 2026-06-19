using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Fluence.Core.Models.Settings;
using Fluence.Core.Models.Theming;

namespace Fluence.Modules.Settings.ViewModels;

internal static class SettingsReflectionScaffolder
{
    public static ObservableCollection<SettingsPropertyViewModel> BuildProperties(
        string sectionName,
        Type settingsType,
        object currentValues,
        ObservableCollection<ThemeDescriptor> themeOptions)
    {
        var items = settingsType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .Select(p =>
            {
                var isTheme = p.GetCustomAttribute<ThemeReferenceAttribute>() is not null;
                return new SettingsPropertyViewModel(
                    p.Name,
                    SettingsDisplayMetadata.GetPropertyDisplayName(sectionName, p.Name),
                    SettingsDisplayMetadata.GetPropertyDescription(sectionName, p.Name),
                    p.PropertyType,
                    p.GetValue(currentValues),
                    isTheme ? themeOptions : null);
            });

        return new ObservableCollection<SettingsPropertyViewModel>(items);
    }
}
