using CommunityToolkit.Mvvm.ComponentModel;

namespace Fluence.Modules.DotnetCli.ViewModels;

public sealed partial class DotnetSdkOptionViewModel(
    string displayName,
    string channel,
    string description,
    bool isRecommended) : ObservableObject
{
    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private string _installedVersion = string.Empty;

    [ObservableProperty]
    private string _installedPath = string.Empty;

    public string DisplayName { get; } = displayName;

    public string Channel { get; } = channel;

    public string Description { get; } = description;

    public bool IsRecommended { get; } = isRecommended;

    public bool CanInstall => !IsInstalled;

    public string StateText => IsInstalled
        ? string.IsNullOrWhiteSpace(InstalledVersion) ? "Installed" : $"Installed {InstalledVersion}"
        : "Available";

    partial void OnIsInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(StateText));
    }

    partial void OnInstalledVersionChanged(string value)
    {
        OnPropertyChanged(nameof(StateText));
    }

    public override string ToString() => DisplayName;
}
