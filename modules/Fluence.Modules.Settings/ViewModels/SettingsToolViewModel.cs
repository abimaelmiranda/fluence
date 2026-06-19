using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.Keybindings;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Abstractions.Theming;
using Fluence.Core.Models.Keybindings;
using Fluence.Core.Models.Settings;
using Fluence.Core.Models.Theming;
using Fluence.Core.Services;
using Fluence.Core.ViewModels;

namespace Fluence.Modules.Settings.ViewModels;

public sealed partial class SettingsToolViewModel : ViewModelBase, ISettingsTool
{
    private readonly ISettingsRegistry _settingsRegistry;
    private readonly ISettingsService _settings;
    private readonly ICommandRegistry _commands;
    private readonly IKeybindingService _keybindings;
    private readonly IThemeLoader _themeLoader;
    private readonly IWorkspaceDialogService _dialogs;
    private readonly ILocalizationService _loc;

    public SettingsToolViewModel(
        ISettingsRegistry settingsRegistry,
        ISettingsService settings,
        ICommandRegistry commands,
        IKeybindingService keybindings,
        IThemeLoader themeLoader,
        IWorkspaceDialogService dialogs,
        ILocalizationService loc)
    {
        _settingsRegistry = settingsRegistry;
        _settings = settings;
        _commands = commands;
        _keybindings = keybindings;
        _themeLoader = themeLoader;
        _dialogs = dialogs;
        _loc = loc;
        _loc.LanguageChanged += OnLanguageChanged;
        Reload();
        _keybindings.Watch().Subscribe(new ActionObserver<IReadOnlyList<KeybindingDefinition>>(
            _ => Dispatcher.UIThread.Post(ReloadKeybindings)));
        _commands.Changed += (_, _) => Dispatcher.UIThread.Post(ReloadKeybindings);
    }

    private void OnLanguageChanged()
    {
        ReloadSettings();
        ReloadKeybindings();
        OnPropertyChanged(nameof(SettingsContextTitle));
        OnPropertyChanged(nameof(Status));
        foreach (var row in Keybindings)
            row.NotifyLanguageChanged();
    }

    [ObservableProperty]
    private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 1)
            ReloadKeybindings();
    }

    private string _statusKey = "Settings.Status.Ready";
    private object[] _statusArgs = [];

    public string Status => _statusArgs.Length == 0
        ? _loc.Get(_statusKey)
        : string.Format(_loc.Get(_statusKey), _statusArgs);

    private void SetStatus(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        OnPropertyChanged(nameof(Status));
    }

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private SettingsSectionViewModel? _selectedSection;

    public string SettingsContextTitle =>
        string.IsNullOrWhiteSpace(SearchQuery)
            ? SelectedSection?.DisplayName ?? _loc.Get("Settings.Tab.Settings")
            : _loc.Get("Settings.Status.SearchResults");

    public ObservableCollection<SettingsSectionViewModel> Sections { get; } = [];

    public ObservableCollection<SettingsSectionViewModel> FilteredSections { get; } = [];

    public ObservableCollection<KeybindingRowViewModel> Keybindings { get; } = [];

    public ObservableCollection<KeybindingRowViewModel> FilteredKeybindings { get; } = [];

    public ObservableCollection<ThemeDescriptor> ThemeOptions { get; } = [];

    public bool IsRecordingKeybinding => Keybindings.Any(row => row.IsRecording);

    partial void OnSearchQueryChanged(string value)
    {
        OnPropertyChanged(nameof(SettingsContextTitle));
        RefreshFilters();
    }

    partial void OnSelectedSectionChanged(SettingsSectionViewModel? value)
    {
        OnPropertyChanged(nameof(SettingsContextTitle));
        RefreshSettingsFilter();
    }

    public void ShowSettings()
    {
        SelectedTabIndex = 0;
        ReloadSettings();
    }

    public void ShowKeybindings()
    {
        SelectedTabIndex = 1;
        ReloadKeybindings();
    }

    public void UpdateKeybinding(string commandId, string scope, string key) =>
        _keybindings.SetKeybinding(commandId, scope, key);

    public bool TryCaptureKeybinding(string? gesture, bool isCancel)
    {
        var row = Keybindings.FirstOrDefault(row => row.IsRecording);
        if (row is null)
            return false;

        row.HandleCapture(gesture, isCancel);
        return true;
    }

    public void CancelKeybindingCapture()
    {
        foreach (var row in Keybindings.Where(row => row.IsRecording))
            row.CancelCapture();

        OnPropertyChanged(nameof(IsRecordingKeybinding));
    }

    internal void StartKeybindingCapture(KeybindingRowViewModel activeRow)
    {
        foreach (var row in Keybindings.Where(row => !ReferenceEquals(row, activeRow)))
            row.CancelCapture();

        activeRow.IsRecording = true;
        OnPropertyChanged(nameof(IsRecordingKeybinding));
    }

    internal void NotifyKeybindingRecordingChanged() =>
        OnPropertyChanged(nameof(IsRecordingKeybinding));

    public void ResetKeybinding(string commandId)
    {
        _keybindings.ResetKeybinding(commandId);
        ReloadKeybindings();
        SetStatus("Settings.Status.KeybindingReset");
    }

    [RelayCommand]
    private void Save()
    {
        var emptySetting = GetEmptyStringSetting();
        if (emptySetting is not null)
        {
            SetStatus("Settings.Status.CannotBeEmpty", emptySetting);
            return;
        }

        SaveSettings();
        SaveKeybindings();
        SetStatus("Settings.Status.Saved");
    }

    [RelayCommand]
    private async Task InstallThemeAsync()
    {
        var path = await _dialogs.PickFileAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var installed = _themeLoader.InstallTheme(path);
            ReloadThemeOptions();
            SelectTheme(installed.Reference);
            SetStatus("Settings.Status.ThemeInstalled", installed.DisplayName);
        }
        catch
        {
            SetStatus("Settings.Status.ThemeInstallFailed");
        }
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        _settings.ResetAll();
        _keybindings.ResetAll();
        Reload();
        SetStatus("Settings.Status.DefaultsRestored");
    }

    private void Reload()
    {
        ReloadSettings();
        ReloadKeybindings();
    }

    private void ReloadSettings()
    {
        var selectedSectionName = SelectedSection?.Name;
        Sections.Clear();
        ReloadThemeOptions();
        foreach (var section in _settingsRegistry.Sections
            .OrderBy(section => SettingsDisplayMetadata.GetSectionSortKey(section.SectionName))
            .ThenBy(section => section.SectionName, StringComparer.Ordinal))
        {
            var current = _settings.Get(section.SettingsType);
            var properties = SettingsReflectionScaffolder.BuildProperties(
                section.SectionName, section.SettingsType, current, ThemeOptions);
            var sectionViewModel = new SettingsSectionViewModel(
                section.SectionName,
                SettingsDisplayMetadata.GetSectionDisplayName(section.SectionName),
                section.SettingsType,
                properties);
            foreach (var property in sectionViewModel.Properties)
                property.PropertyChanged += OnSettingPropertyChanged;

            Sections.Add(sectionViewModel);
        }

        SelectedSection = Sections.FirstOrDefault(section =>
            string.Equals(section.Name, selectedSectionName, StringComparison.OrdinalIgnoreCase))
            ?? Sections.FirstOrDefault();
        OnPropertyChanged(nameof(SettingsContextTitle));
        RefreshSettingsFilter();
    }

    private void OnSettingPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SearchQuery))
            RefreshSettingsFilter();
    }

    private void ReloadKeybindings()
    {
        Keybindings.Clear();
        var conflicts = _keybindings.GetConflicts();
        foreach (var binding in _keybindings.GetKeybindings())
        {
            var command = _commands.Find(binding.Command);
            var hasConflict = conflicts.Any(c =>
                string.Equals(c.Scope, binding.Scope, StringComparison.Ordinal) &&
                string.Equals(c.Key, binding.Key, StringComparison.OrdinalIgnoreCase));
            Keybindings.Add(new KeybindingRowViewModel(
                binding.Command,
                command?.Title ?? binding.Command,
                binding.Scope,
                binding.Key,
                hasConflict,
                this,
                _loc));
        }

        RefreshKeybindingsFilter();
    }

    private void RefreshFilters()
    {
        RefreshSettingsFilter();
        RefreshKeybindingsFilter();
    }

    private void RefreshSettingsFilter()
    {
        FilteredSections.Clear();
        var query = NormalizeSearch(SearchQuery);
        foreach (var section in Sections)
        {
            section.FilteredProperties.Clear();

            if (string.IsNullOrWhiteSpace(query) && !ReferenceEquals(section, SelectedSection))
                continue;

            if (string.IsNullOrWhiteSpace(query) || ContainsSearch(section.Name, query) || ContainsSearch(section.DisplayName, query))
            {
                foreach (var property in section.Properties)
                    section.FilteredProperties.Add(property);
            }
            else
            {
                foreach (var property in section.Properties.Where(property => MatchesSettingProperty(property, query)))
                    section.FilteredProperties.Add(property);
            }

            if (section.FilteredProperties.Count > 0)
                FilteredSections.Add(section);
        }
    }

    private void RefreshKeybindingsFilter()
    {
        FilteredKeybindings.Clear();
        var query = NormalizeSearch(SearchQuery);
        foreach (var keybinding in Keybindings)
        {
            if (string.IsNullOrWhiteSpace(query) || MatchesKeybinding(keybinding, query))
                FilteredKeybindings.Add(keybinding);
        }
    }

    private static bool MatchesSettingProperty(SettingsPropertyViewModel property, string query) =>
        ContainsSearch(property.Name, query) ||
        ContainsSearch(property.DisplayName, query) ||
        ContainsSearch(property.Description, query) ||
        ContainsSearch(property.TextValue, query) ||
        ContainsSearch(property.ThemeReference, query) ||
        ContainsSearch(property.SelectedTheme?.DisplayName, query) ||
        ContainsSearch(property.SelectedTheme?.Reference, query) ||
        ContainsSearch(property.StringArraySearchText, query) ||
        ContainsSearch(property.BoolValue.ToString(), query);

    private static bool MatchesKeybinding(KeybindingRowViewModel keybinding, string query) =>
        ContainsSearch(keybinding.Title, query) ||
        ContainsSearch(keybinding.CommandId, query) ||
        ContainsSearch(keybinding.Scope, query) ||
        ContainsSearch(keybinding.Key, query);

    private static string NormalizeSearch(string? value) =>
        value?.Trim() ?? string.Empty;

    private static bool ContainsSearch(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void SaveSettings()
    {
        foreach (var section in Sections.ToArray())
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var item in section.Properties)
            {
                var property = section.SettingsType.GetProperty(item.Name, BindingFlags.Public | BindingFlags.Instance);
                if (property is null || !property.CanWrite)
                    continue;

                values[item.Name] = SettingsValueConverter.Convert(property, item);
            }

            _settings.ReplaceSection(section.SettingsType, values);
        }
    }

    private void SaveKeybindings()
    {
        foreach (var item in Keybindings.ToArray())
            _keybindings.SetKeybinding(item.CommandId, item.Scope, item.Key);
    }

    private string? GetEmptyStringSetting()
    {
        foreach (var section in Sections)
        {
            var item = section.Properties.FirstOrDefault(item =>
                item.ValueType == typeof(string) && string.IsNullOrWhiteSpace(item.TextValue));
            if (item is not null)
                return $"{section.Name}.{item.Name}";
        }

        return null;
    }

    private void ReloadThemeOptions()
    {
        ThemeOptions.Clear();
        foreach (var theme in _themeLoader.GetAvailableThemes())
            ThemeOptions.Add(theme);
    }

    private void SelectTheme(string reference)
    {
        foreach (var section in Sections)
        {
            if (section.SettingsType != typeof(GlobalSettings))
                continue;

            var theme = section.Properties.FirstOrDefault(item => item.IsThemeSelector);
            theme?.SelectTheme(reference);
        }
    }
}
