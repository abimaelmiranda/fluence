using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Core.Models.Theming;

namespace Fluence.Modules.Settings.ViewModels;

public sealed partial class SettingsPropertyViewModel(
    string name,
    Type valueType,
    object? value,
    ObservableCollection<ThemeDescriptor>? themeOptions = null) : ObservableObject
{
    public string Name { get; } = name;

    public Type ValueType { get; } = valueType;

    public bool IsThemeSelector => themeOptions is not null;

    public bool IsBoolean => ValueType == typeof(bool);

    public bool IsEnum => ValueType.IsEnum;

    public bool IsText => !IsBoolean && !IsThemeSelector && !IsEnum;

    public ObservableCollection<ThemeDescriptor> ThemeOptions { get; } = themeOptions ?? [];

    public ObservableCollection<string> EnumOptions { get; } = valueType.IsEnum
        ? new ObservableCollection<string>(Enum.GetNames(valueType))
        : [];

    [ObservableProperty]
    private string _textValue = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    [ObservableProperty]
    private bool _boolValue = value is bool boolValue && boolValue;

    [ObservableProperty]
    private string _selectedEnumValue = valueType.IsEnum
        ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? Enum.GetNames(valueType).FirstOrDefault() ?? string.Empty
        : string.Empty;

    [ObservableProperty]
    private ThemeDescriptor? _selectedTheme = themeOptions is null
        ? null
        : FindTheme(themeOptions, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));

    public string ThemeReference => SelectedTheme?.Reference ?? TextValue;

    public void SelectTheme(string reference)
    {
        if (!IsThemeSelector)
            return;

        SelectedTheme = FindTheme(ThemeOptions, reference);
        TextValue = SelectedTheme?.Reference ?? reference;
    }

    partial void OnSelectedThemeChanged(ThemeDescriptor? value)
    {
        if (value is not null)
            TextValue = value.Reference;
    }

    partial void OnSelectedEnumValueChanged(string value)
    {
        if (IsEnum)
            TextValue = value;
    }

    private static ThemeDescriptor? FindTheme(
        ObservableCollection<ThemeDescriptor> options,
        string? reference)
    {
        if (options.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(reference))
        {
            foreach (var option in options)
            {
                if (string.Equals(option.Reference, reference, StringComparison.OrdinalIgnoreCase))
                    return option;
            }
        }

        return options[0];
    }
}
