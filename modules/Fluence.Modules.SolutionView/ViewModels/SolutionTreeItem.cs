using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Modules.SolutionView.Models;
using Fluence.Modules.SolutionView.Models.Enums;

namespace Fluence.Modules.SolutionView.ViewModels;

public sealed partial class SolutionTreeItem : ObservableObject
{
    private static readonly SolutionTreeItem LoadingPlaceholder = new(
        SolutionTreeNodeKind.File,
        "Loading...",
        null,
        null);

    private readonly Action<SolutionTreeItem>? _activate;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoadingChildren;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private bool _isStartupProject;

    public SolutionTreeItem(
        SolutionTreeNodeKind kind,
        string name,
        string? path,
        Action<SolutionTreeItem>? activate,
        ICommand? openCommand = null,
        ICommand? newFileCommand = null,
        ICommand? newFolderCommand = null,
        ICommand? copyCommand = null,
        ICommand? pasteCommand = null,
        ICommand? deleteCommand = null,
        ICommand? loadSolutionCommand = null,
        ICommand? closeSolutionCommand = null,
        ICommand? manageNuGetPackagesCommand = null,
        ICommand? buildCommand = null,
        ICommand? restoreCommand = null,
        ICommand? cleanCommand = null,
        ICommand? publishCommand = null,
        ICommand? runCommand = null,
        ICommand? testCommand = null,
        ICommand? setStartupProjectCommand = null,
        ICommand? addProjectReferenceCommand = null,
        ICommand? removeProjectReferenceCommand = null,
        Func<SolutionTreeItem, CancellationToken, Task>? loadChildren = null,
        string? projectPath = null,
        string? referencedProjectPath = null,
        bool isResolved = true,
        bool areChildrenLoaded = true)
    {
        Kind = kind;
        Name = name;
        Path = path;
        _activate = activate;
        OpenCommand = openCommand;
        NewFileCommand = newFileCommand;
        NewFolderCommand = newFolderCommand;
        CopyCommand = copyCommand;
        PasteCommand = pasteCommand;
        DeleteCommand = deleteCommand;
        LoadSolutionCommand = loadSolutionCommand;
        CloseSolutionCommand = closeSolutionCommand;
        ManageNuGetPackagesCommand = manageNuGetPackagesCommand;
        BuildCommand = buildCommand;
        RestoreCommand = restoreCommand;
        CleanCommand = cleanCommand;
        PublishCommand = publishCommand;
        RunCommand = runCommand;
        TestCommand = testCommand;
        SetStartupProjectCommand = setStartupProjectCommand;
        AddProjectReferenceCommand = addProjectReferenceCommand;
        RemoveProjectReferenceCommand = removeProjectReferenceCommand;
        LoadChildrenCommand = loadChildren is null
            ? null
            : new AsyncRelayCommand(token => LoadChildrenAsync(loadChildren, token));
        ProjectPath = projectPath;
        ReferencedProjectPath = referencedProjectPath;
        IsResolved = isResolved;
        AreChildrenLoaded = areChildrenLoaded;

        if (!AreChildrenLoaded)
            Children.Add(LoadingPlaceholder);
    }

    public SolutionTreeNodeKind Kind { get; }
    public string Name { get; }
    public string? Path { get; }
    public string? ProjectPath { get; }
    public string? ReferencedProjectPath { get; }
    public bool IsResolved { get; }
    public bool AreChildrenLoaded { get; private set; }
    public ObservableCollection<SolutionTreeItem> Children { get; } = [];
    public ICommand? OpenCommand { get; set; }
    public ICommand? NewFileCommand { get; }
    public ICommand? NewFolderCommand { get; }
    public ICommand? CopyCommand { get; set; }
    public ICommand? PasteCommand { get; set; }
    public ICommand? DeleteCommand { get; set; }
    public ICommand? LoadSolutionCommand { get; set; }
    public ICommand? CloseSolutionCommand { get; set; }
    public ICommand? ManageNuGetPackagesCommand { get; }
    public ICommand? BuildCommand { get; }
    public ICommand? RestoreCommand { get; }
    public ICommand? CleanCommand { get; }
    public ICommand? PublishCommand { get; }
    public ICommand? RunCommand { get; }
    public ICommand? TestCommand { get; }
    public ICommand? SetStartupProjectCommand { get; }
    public ICommand? AddProjectReferenceCommand { get; }
    public ICommand? RemoveProjectReferenceCommand { get; }
    public ICommand? LoadChildrenCommand { get; }

    public bool IsFile => Kind == SolutionTreeNodeKind.File;
    public bool IsProject => Kind == SolutionTreeNodeKind.Project;
    public bool IsSolution => Kind == SolutionTreeNodeKind.Solution;
    public bool IsProjectReference => Kind == SolutionTreeNodeKind.ProjectReference;
    public bool HasOpenCommand => OpenCommand is not null;
    public bool HasNewFileCommand => NewFileCommand is not null;
    public bool HasNewFolderCommand => NewFolderCommand is not null;
    public bool HasCopyCommand => CopyCommand is not null;
    public bool HasPasteCommand => PasteCommand is not null;
    public bool HasDeleteCommand => DeleteCommand is not null;
    public bool HasLoadSolutionCommand => LoadSolutionCommand is not null;
    public bool HasCloseSolutionCommand => CloseSolutionCommand is not null;
    public bool HasManageNuGetPackagesCommand => ManageNuGetPackagesCommand is not null;
    public bool HasContextMenu => HasOpenCommand ||
                                  HasNewFileCommand ||
                                  HasNewFolderCommand ||
                                  HasCopyCommand ||
                                  HasPasteCommand ||
                                  HasDeleteCommand ||
                                  HasLoadSolutionCommand ||
                                  HasCloseSolutionCommand ||
                                  HasManageNuGetPackagesCommand ||
                                  HasAdvancedCommands;
    public bool HasAdvancedCommands => HasBuildCommand ||
                                       HasRestoreCommand ||
                                       HasCleanCommand ||
                                       HasPublishCommand ||
                                       HasRunCommand ||
                                       HasTestCommand ||
                                       HasSetStartupProjectCommand ||
                                       HasAddProjectReferenceCommand ||
                                       HasRemoveProjectReferenceCommand;
    public bool HasBuildCommand => BuildCommand is not null;
    public bool HasRestoreCommand => RestoreCommand is not null;
    public bool HasCleanCommand => CleanCommand is not null;
    public bool HasPublishCommand => PublishCommand is not null;
    public bool HasRunCommand => RunCommand is not null;
    public bool HasTestCommand => TestCommand is not null;
    public bool HasSetStartupProjectCommand => SetStartupProjectCommand is not null;
    public bool HasAddProjectReferenceCommand => AddProjectReferenceCommand is not null;
    public bool HasRemoveProjectReferenceCommand => RemoveProjectReferenceCommand is not null;

    public string DisplayName => IsStartupProject ? $"{Name} (startup)" : Name;

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || AreChildrenLoaded || LoadChildrenCommand is null || !LoadChildrenCommand.CanExecute(null))
            return;

        LoadChildrenCommand.Execute(null);
    }

    public void MarkChildrenLoaded()
    {
        AreChildrenLoaded = true;
    }

    public void ResetPlaceholder()
    {
        Children.Clear();
        AreChildrenLoaded = false;
        Children.Add(LoadingPlaceholder);
    }

    public void Activate() => _activate?.Invoke(this);

    private async Task LoadChildrenAsync(
        Func<SolutionTreeItem, CancellationToken, Task> loadChildren,
        CancellationToken cancellationToken)
    {
        if (IsLoadingChildren)
            return;

        try
        {
            IsLoadingChildren = true;
            await loadChildren(this, cancellationToken);
        }
        finally
        {
            IsLoadingChildren = false;
        }
    }
}
