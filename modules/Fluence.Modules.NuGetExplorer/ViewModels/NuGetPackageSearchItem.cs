using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Modules.NuGetExplorer.Models;

namespace Fluence.Modules.NuGetExplorer.ViewModels;

public sealed partial class NuGetPackageSearchItem(NuGetPackageSearchResult package) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private Bitmap? _icon;

    public NuGetPackageSearchResult Package { get; } = package;

    public string Id => Package.Id;

    public string Version => Package.Version;

    public string? Description => Package.Description;

    public string? Authors => Package.Authors;

    public string? IconUrl => Package.IconUrl;

    public bool HasIcon => Icon is not null;
}
