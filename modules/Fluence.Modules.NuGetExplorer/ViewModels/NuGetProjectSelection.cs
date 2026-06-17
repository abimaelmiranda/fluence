using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Modules.NuGetExplorer.Models;

namespace Fluence.Modules.NuGetExplorer.ViewModels;

public sealed partial class NuGetProjectSelection(NuGetProject project) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayIsChecked))]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayIsChecked))]
    private bool _isAlreadyInstalled;

    public NuGetProject Project { get; } = project;

    public string Name => Project.Name;

    public string ProjectPath => Project.ProjectPath;

    public bool DisplayIsChecked
    {
        get => IsAlreadyInstalled || IsSelected;
        set
        {
            if (!IsAlreadyInstalled)
                IsSelected = value;
        }
    }
}
