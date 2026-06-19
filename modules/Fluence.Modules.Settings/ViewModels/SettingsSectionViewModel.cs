using System;
using System.Collections.ObjectModel;

namespace Fluence.Modules.Settings.ViewModels;

public sealed class SettingsSectionViewModel(
    string name,
    string displayName,
    Type settingsType,
    ObservableCollection<SettingsPropertyViewModel> properties)
{
    public string Name { get; } = name;

    public string DisplayName { get; } = displayName;

    public Type SettingsType { get; } = settingsType;

    public ObservableCollection<SettingsPropertyViewModel> Properties { get; } = properties;

    public ObservableCollection<SettingsPropertyViewModel> FilteredProperties { get; } = [];
}
