using System;
using System.Collections.ObjectModel;

namespace Fluence.Modules.Settings.ViewModels;

public sealed class SettingsSectionViewModel(
    string name,
    Type settingsType,
    ObservableCollection<SettingsPropertyViewModel> properties)
{
    public string Name { get; } = name;

    public Type SettingsType { get; } = settingsType;

    public ObservableCollection<SettingsPropertyViewModel> Properties { get; } = properties;
}
