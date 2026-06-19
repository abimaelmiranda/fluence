using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Models.Theming;

namespace Fluence.Modules.Settings.ViewModels;

public sealed partial class SettingsPropertyViewModel : ObservableObject
{
    public SettingsPropertyViewModel(
        string name,
        string displayName,
        string description,
        Type valueType,
        object? value,
        ObservableCollection<ThemeDescriptor>? themeOptions = null)
    {
        Name = name;
        DisplayName = displayName;
        Description = description;
        ValueType = valueType;
        IsThemeSelector = themeOptions is not null;
        ThemeOptions = themeOptions ?? [];
        EnumOptions = valueType.IsEnum
            ? new ObservableCollection<string>(Enum.GetNames(valueType))
            : [];
        TextValue = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        BoolValue = value is bool boolValue && boolValue;
        SelectedEnumValue = valueType.IsEnum
            ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                ?? Enum.GetNames(valueType).FirstOrDefault()
                ?? string.Empty
            : string.Empty;
        SelectedTheme = themeOptions is null
            ? null
            : FindTheme(themeOptions, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));

        foreach (var item in NormalizeArrayValues(value))
            StringArrayItems.Add(new SettingsStringArrayItemViewModel(item, RemoveStringArrayItem));

        if (IsStringArray)
        {
            foreach (var option in KnownStringArrayOptions)
            {
                KnownStringArrayRuleItems.Add(new SettingsKnownStringArrayOptionViewModel(
                    option,
                    ContainsStringArrayValue(option.Id),
                    SetKnownStringArraySuppressed));
            }
        }

        StringArrayItems.CollectionChanged += OnStringArrayItemsChanged;
        foreach (var item in StringArrayItems)
            item.PropertyChanged += OnStringArrayItemChanged;
    }

    public string Name { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public Type ValueType { get; }

    public bool IsThemeSelector { get; }

    public bool IsBoolean => ValueType == typeof(bool);

    public bool IsEnum => ValueType.IsEnum;

    public bool IsStringArray => ValueType == typeof(string[]);

    public bool IsWideEditor => IsStringArray;

    public bool IsCompactEditor => !IsWideEditor;

    public bool IsLabeledCompact => IsCompactEditor && !IsBoolean;

    public bool IsText => !IsBoolean && !IsThemeSelector && !IsEnum && !IsStringArray;

    public ObservableCollection<ThemeDescriptor> ThemeOptions { get; }

    public ObservableCollection<string> EnumOptions { get; }

    public ObservableCollection<SettingsStringArrayOption> KnownStringArrayOptions => RoslynRuleCatalog.KnownRules;

    public ObservableCollection<SettingsKnownStringArrayOptionViewModel> KnownStringArrayRuleItems { get; } = [];

    public ObservableCollection<SettingsStringArrayItemViewModel> StringArrayItems { get; } = [];

    public SettingsStringArrayItemViewModel[] CustomStringArrayItems =>
        [.. StringArrayItems.Where(item => !item.IsKnownRule)];

    public SettingsKnownStringArrayOptionViewModel[] AvailableKnownRulesToAdd =>
        [.. KnownStringArrayRuleItems.Where(r => !r.IsSuppressed)];

    [ObservableProperty]
    private SettingsKnownStringArrayOptionViewModel? _selectedKnownRuleToAdd;

    [ObservableProperty]
    private string _textValue = string.Empty;

    [ObservableProperty]
    private bool _boolValue;

    [ObservableProperty]
    private string _selectedEnumValue = string.Empty;

    [ObservableProperty]
    private ThemeDescriptor? _selectedTheme;

    [ObservableProperty]
    private string _newStringArrayValue = string.Empty;

    public string ThemeReference => SelectedTheme?.Reference ?? TextValue;

    public string StringArraySearchText => string.Join(' ', StringArrayItems.Select(item => item.Value));

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

    [RelayCommand]
    private void AddCustomStringArrayValue() => AddStringArrayValue(NewStringArrayValue);

    [RelayCommand]
    private void AddSelectedKnownRule()
    {
        if (SelectedKnownRuleToAdd is null) return;
        AddStringArrayValue(SelectedKnownRuleToAdd.Id);
        SelectedKnownRuleToAdd = null;
    }

    public string[] GetStringArrayValues() =>
        StringArrayItems
            .Select(item => item.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private void AddStringArrayValue(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        if (StringArrayItems.Any(item => string.Equals(item.Value.Trim(), normalized, StringComparison.OrdinalIgnoreCase)))
        {
            NewStringArrayValue = string.Empty;
            return;
        }

        StringArrayItems.Add(new SettingsStringArrayItemViewModel(normalized, RemoveStringArrayItem));
        NewStringArrayValue = string.Empty;
        RefreshStringArrayState();
    }

    private void SetKnownStringArraySuppressed(
        SettingsKnownStringArrayOptionViewModel option,
        bool isSuppressed)
    {
        if (isSuppressed)
        {
            AddStringArrayValue(option.Id);
            return;
        }

        var existing = StringArrayItems.FirstOrDefault(item =>
            string.Equals(item.Value.Trim(), option.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            StringArrayItems.Remove(existing);

        RefreshStringArrayState();
    }

    private void RemoveStringArrayItem(SettingsStringArrayItemViewModel item)
    {
        StringArrayItems.Remove(item);
        RefreshStringArrayState();
    }

    private void OnStringArrayItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (SettingsStringArrayItemViewModel item in e.NewItems)
                item.PropertyChanged += OnStringArrayItemChanged;
        }

        if (e.OldItems is not null)
        {
            foreach (SettingsStringArrayItemViewModel item in e.OldItems)
                item.PropertyChanged -= OnStringArrayItemChanged;
        }

        RefreshStringArrayState();
    }

    private void OnStringArrayItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsStringArrayItemViewModel.Value))
            RefreshStringArrayState();
    }

    private bool ContainsStringArrayValue(string value) =>
        StringArrayItems.Any(item =>
            string.Equals(item.Value.Trim(), value, StringComparison.OrdinalIgnoreCase));

    private void RefreshStringArrayState()
    {
        foreach (var option in KnownStringArrayRuleItems)
            option.SetSuppressedSilently(ContainsStringArrayValue(option.Id));

        OnPropertyChanged(nameof(CustomStringArrayItems));
        OnPropertyChanged(nameof(AvailableKnownRulesToAdd));
        OnPropertyChanged(nameof(StringArraySearchText));
    }

    private static string[] NormalizeArrayValues(object? value) =>
        value is string[] items
            ? [.. items
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)]
            : [];

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
