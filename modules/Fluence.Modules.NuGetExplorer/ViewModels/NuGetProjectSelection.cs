using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Modules.NuGetExplorer.Models;

namespace Fluence.Modules.NuGetExplorer.ViewModels;

public sealed partial class NuGetProjectSelection(NuGetProject project) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public NuGetProject Project { get; } = project;

    public string Name => Project.Name;

    public string ProjectPath => Project.ProjectPath;
}
