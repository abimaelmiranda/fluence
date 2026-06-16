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

    public SettingsToolViewModel(
        ISettingsRegistry settingsRegistry,
        ISettingsService settings,
        ICommandRegistry commands,
        IKeybindingService keybindings,
        IThemeLoader themeLoader,
        IWorkspaceDialogService dialogs)
    {
        _settingsRegistry = settingsRegistry;
        _settings = settings;
        _commands = commands;
        _keybindings = keybindings;
        _themeLoader = themeLoader;
        _dialogs = dialogs;
        Reload();
        _keybindings.Watch().Subscribe(new ActionObserver<IReadOnlyList<KeybindingDefinition>>(
            _ => Dispatcher.UIThread.Post(ReloadKeybindings)));
        _commands.Changed += (_, _) => Dispatcher.UIThread.Post(ReloadKeybindings);
    }

    [ObservableProperty]
    private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 1)
            ReloadKeybindings();
    }

    [ObservableProperty]
    private string _status = "Ready";

    public ObservableCollection<SettingsSectionViewModel> Sections { get; } = [];

    public ObservableCollection<KeybindingRowViewModel> Keybindings { get; } = [];

    public ObservableCollection<ThemeDescriptor> ThemeOptions { get; } = [];

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

    public void ResetKeybinding(string commandId)
    {
        _keybindings.ResetKeybinding(commandId);
        ReloadKeybindings();
        Status = "Keybinding reset";
    }

    [RelayCommand]
    private void Save()
    {
        var emptySetting = GetEmptyStringSetting();
        if (emptySetting is not null)
        {
            Status = $"{emptySetting} cannot be empty";
            return;
        }

        SaveSettings();
        SaveKeybindings();
        Status = "Saved";
        Reload();
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
            Status = $"Installed {installed.DisplayName}";
        }
        catch
        {
            Status = "Theme install failed";
        }
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        _settings.ResetAll();
        _keybindings.ResetAll();
        Reload();
        Status = "Defaults restored";
    }

    private void Reload()
    {
        ReloadSettings();
        ReloadKeybindings();
    }

    private void ReloadSettings()
    {
        Sections.Clear();
        ReloadThemeOptions();
        foreach (var section in _settingsRegistry.Sections)
        {
            var current = _settings.Get(section.SettingsType);
            var properties = SettingsReflectionScaffolder.BuildProperties(
                section.SettingsType, current, ThemeOptions);
            Sections.Add(new SettingsSectionViewModel(section.SectionName, section.SettingsType, properties));
        }
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
                this));
        }
    }

    private void SaveSettings()
    {
        foreach (var section in Sections)
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
