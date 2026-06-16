using System;
using System.Collections.ObjectModel;
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

    public bool IsText => !IsBoolean && !IsThemeSelector;

    public ObservableCollection<ThemeDescriptor> ThemeOptions { get; } = themeOptions ?? [];

    [ObservableProperty]
    private string _textValue = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    [ObservableProperty]
    private bool _boolValue = value is bool boolValue && boolValue;

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
