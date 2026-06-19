using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Core.Abstractions.Localization;

namespace Fluence.Modules.DotnetCli.ViewModels;

public sealed partial class DotnetSdkOptionViewModel(
    string displayName,
    string channel,
    string description,
    bool isRecommended,
    ILocalizationService localization) : ObservableObject
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

    private ILocalizationService Loc { get; } = localization;

    public bool CanInstall => !IsInstalled;

    public string StateText => IsInstalled
        ? string.IsNullOrWhiteSpace(InstalledVersion)
            ? Loc.Get("DotnetCli.Status.Installed")
            : string.Format(Loc.Get("DotnetCli.Status.InstalledWithVersion"), InstalledVersion)
        : Loc.Get("DotnetCli.Status.Available");

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
