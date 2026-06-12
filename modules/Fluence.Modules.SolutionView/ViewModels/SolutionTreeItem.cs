using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Fluence.Modules.SolutionView.ViewModels;

public sealed partial class SolutionTreeItem : ObservableObject
{
    private readonly Action<SolutionTreeItem>? _activate;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private bool _isStartupProject;

    public SolutionTreeItem(
        SolutionTreeNodeKind kind,
        string name,
        string? path,
        Action<SolutionTreeItem>? activate,
        ICommand? buildCommand = null,
        ICommand? restoreCommand = null,
        ICommand? cleanCommand = null,
        ICommand? runCommand = null,
        ICommand? testCommand = null,
        ICommand? setStartupProjectCommand = null,
        ICommand? addProjectReferenceCommand = null,
        ICommand? removeProjectReferenceCommand = null,
        string? projectPath = null,
        string? referencedProjectPath = null,
        bool isResolved = true)
    {
        Kind = kind;
        Name = name;
        Path = path;
        _activate = activate;
        BuildCommand = buildCommand;
        RestoreCommand = restoreCommand;
        CleanCommand = cleanCommand;
        RunCommand = runCommand;
        TestCommand = testCommand;
        SetStartupProjectCommand = setStartupProjectCommand;
        AddProjectReferenceCommand = addProjectReferenceCommand;
        RemoveProjectReferenceCommand = removeProjectReferenceCommand;
        ProjectPath = projectPath;
        ReferencedProjectPath = referencedProjectPath;
        IsResolved = isResolved;
    }

    public SolutionTreeNodeKind Kind { get; }
    public string Name { get; }
    public string? Path { get; }
    public string? ProjectPath { get; }
    public string? ReferencedProjectPath { get; }
    public bool IsResolved { get; }
    public ObservableCollection<SolutionTreeItem> Children { get; } = [];
    public ICommand? BuildCommand { get; }
    public ICommand? RestoreCommand { get; }
    public ICommand? CleanCommand { get; }
    public ICommand? RunCommand { get; }
    public ICommand? TestCommand { get; }
    public ICommand? SetStartupProjectCommand { get; }
    public ICommand? AddProjectReferenceCommand { get; }
    public ICommand? RemoveProjectReferenceCommand { get; }

    public bool IsFile => Kind == SolutionTreeNodeKind.File;
    public bool IsProject => Kind == SolutionTreeNodeKind.Project;
    public bool IsSolution => Kind == SolutionTreeNodeKind.Solution;
    public bool IsProjectReference => Kind == SolutionTreeNodeKind.ProjectReference;
    public bool HasBuildCommand => BuildCommand is not null;
    public bool HasRestoreCommand => RestoreCommand is not null;
    public bool HasCleanCommand => CleanCommand is not null;
    public bool HasRunCommand => RunCommand is not null;
    public bool HasTestCommand => TestCommand is not null;
    public bool HasSetStartupProjectCommand => SetStartupProjectCommand is not null;
    public bool HasAddProjectReferenceCommand => AddProjectReferenceCommand is not null;
    public bool HasRemoveProjectReferenceCommand => RemoveProjectReferenceCommand is not null;

    public string DisplayName => IsStartupProject ? $"{Name} (startup)" : Name;

    public void Activate() => _activate?.Invoke(this);
}
