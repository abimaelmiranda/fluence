using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.File;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Services.File;
using Fluence.Core.ViewModels;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Models.Workbench;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.Workbench.SolutionView.Abstractions;
using Fluence.Modules.Workbench.SolutionView.Commands;
using Fluence.Modules.Workbench.SolutionView.Commands.AddProjectReference;
using Fluence.Modules.Workbench.SolutionView.Commands.RemoveProjectReference;
using Fluence.Modules.Workbench.SolutionView.Commands.SetStartupProject;
using Fluence.Modules.Workbench.SolutionView.Exceptions;
using Fluence.Modules.Workbench.SolutionView.Models;
using Fluence.Modules.Workbench.SolutionView.Models.Enums;
using Fluence.Modules.Workbench.SolutionView.Services;
using Fluence.Core.Events.Build;
using Fluence.Core.Events.Ui;
using Fluence.Core.Events.Workspace;

namespace Fluence.Modules.Workbench.SolutionView.ViewModels;

public sealed partial class SolutionViewModel : ViewModelBase
{
    private readonly IWorkspaceContext _workspace;
    private readonly ISolutionWorkspaceLoader _solutionLoader;
    private readonly ISolutionStructureService _solutionStructure;
    private readonly ICommandHandler<AddProjectReferencesCommand> _addProjectReferencesHandler;
    private readonly ICommandHandler<RemoveProjectReferenceCommand> _removeProjectReferenceHandler;
    private readonly ICommandHandler<SetStartupProjectCommand> _setStartupProjectHandler;
    private readonly IProjectReferenceService _projectReferences;
    private readonly IProjectReferenceDialogService _referenceDialogs;
    private readonly ISolutionFileCreationDialogService _creationDialogs;
    private readonly IUserNotificationService _notifications;
    private readonly IFileClipboardService _clipboard;
    private readonly IFileOperationDialogService _fileDialogs;
    private readonly IFileService _fileService;
    private readonly IShellEventBus _eventBus;
    private readonly SolutionProjectAssociationService _projectAssociations;
    private CancellationTokenSource? _loadCts;
    private readonly object _loadLock = new();
    private readonly Dictionary<string, CancellationTokenSource> _projectLoadCtsByPath = new(StringComparer.OrdinalIgnoreCase);
    private string? _loadedSolutionPath;
    private SolutionWorkspaceSnapshot? _loadedSnapshot;
    private HashSet<string>? _expandedTreeKeys;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public SolutionViewModel(
        IWorkspaceContext workspace,
        ISolutionWorkspaceLoader solutionLoader,
        ISolutionStructureService solutionStructure,
        ICommandHandler<AddProjectReferencesCommand> addProjectReferencesHandler,
        ICommandHandler<RemoveProjectReferenceCommand> removeProjectReferenceHandler,
        ICommandHandler<SetStartupProjectCommand> setStartupProjectHandler,
        IProjectReferenceService projectReferences,
        IProjectReferenceDialogService referenceDialogs,
        ISolutionFileCreationDialogService creationDialogs,
        IUserNotificationService notifications,
        IFileClipboardService clipboard,
        IFileOperationDialogService fileDialogs,
        IFileService fileService,
        IShellEventBus eventBus,
        SolutionProjectAssociationService projectAssociations)
    {
        _workspace = workspace;
        _solutionLoader = solutionLoader;
        _solutionStructure = solutionStructure;
        _addProjectReferencesHandler = addProjectReferencesHandler;
        _removeProjectReferenceHandler = removeProjectReferenceHandler;
        _setStartupProjectHandler = setStartupProjectHandler;
        _projectReferences = projectReferences;
        _referenceDialogs = referenceDialogs;
        _creationDialogs = creationDialogs;
        _notifications = notifications;
        _clipboard = clipboard;
        _fileDialogs = fileDialogs;
        _fileService = fileService;
        _eventBus = eventBus;
        _projectAssociations = projectAssociations;
        _workspace.Changed += OnWorkspaceChanged;
        RefreshForWorkspace();
    }

    public ObservableCollection<SolutionTreeItem> RootItems { get; } = [];

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        RefreshForWorkspace();
        UpdateActiveItem(_workspace.Current.TabSession.ActiveDocument?.Path);
        UpdateStartupProject(_workspace.Current.StartupProjectPath);
    }

    private void RefreshForWorkspace()
    {
        var workspace = _workspace.Current;
        var solutionPath = workspace.CurrentSolutionPath;
        if (workspace.NavigationMode != WorkspaceMode.Solution || string.IsNullOrWhiteSpace(solutionPath))
        {
            _loadedSolutionPath = null;
            _loadedSnapshot = null;
            CancelProjectLoads();
            _projectAssociations.Clear();
            RootItems.Clear();
            ErrorMessage = null;
            return;
        }

        if (string.Equals(_loadedSolutionPath, solutionPath, StringComparison.OrdinalIgnoreCase))
            return;

        _loadedSolutionPath = solutionPath;
        _ = LoadAsync(solutionPath);
    }

    private async Task LoadAsync(string solutionPath)
    {
        CancellationToken cancellationToken;
        var expandedTreeKeys = CaptureExpandedTreeState();
        lock (_loadLock)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
            cancellationToken = _loadCts.Token;
        }

        _expandedTreeKeys = expandedTreeKeys;
        IsLoading = true;
        ErrorMessage = null;
        CancelProjectLoads();
        _projectAssociations.Clear(solutionPath);

        try
        {
            var snapshot = await _solutionLoader.LoadStructuralAsync(solutionPath, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            _loadedSnapshot = snapshot;
            _projectAssociations.UpdateStructural(snapshot);

            if (cancellationToken.IsCancellationRequested)
                return;

            var rootItem = CreateTreeItem(snapshot.Root, deferProjectChildren: true);
            RootItems.Clear();
            RootItems.Add(rootItem);
            RestoreExpandedTreeState(RootItems, _expandedTreeKeys);
            UpdateActiveItem(_workspace.Current.TabSession.ActiveDocument?.Path);
            UpdateStartupProject(_workspace.Current.StartupProjectPath);
        }
        catch (OperationCanceledException) { }
        catch (SolutionWorkspaceLoadException ex)
        {
            ErrorMessage = ex.Reason;
            _notifications.ShowError("Unable to load solution", ex.Reason);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            ErrorMessage = "The solution could not be read.";
            _notifications.ShowError("Unable to load solution", ErrorMessage);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private SolutionTreeItem CreateTreeItem(SolutionTreeNode node, bool deferProjectChildren)
    {
        var shouldDeferChildren = deferProjectChildren &&
                                  node.Kind == SolutionTreeNodeKind.Project &&
                                  node.Path is not null;

        var item = new SolutionTreeItem(
            node.Kind,
            node.Name,
            node.Path,
            ActivateItem,
            CreateOpenCommand(node),
            CreateNewFileCommand(node),
            CreateNewFolderCommand(node),
            CreateCopyCommand(node),
            CreatePasteCommand(node),
            CreateDeleteCommand(node),
            CreateLoadSolutionCommand(node),
            CreateCloseSolutionCommand(node),
            CreateManageNuGetPackagesCommand(node),
            CreateBuildCommand(node),
            CreateRestoreCommand(node),
            CreateCleanCommand(node),
            CreatePublishCommand(node),
            CreateRunCommand(node),
            CreateTestCommand(node),
            CreateSetStartupProjectCommand(node),
            CreateAddProjectReferenceCommand(node),
            CreateRemoveProjectReferenceCommand(node),
            shouldDeferChildren ? LoadProjectChildrenAsync : null,
            node.ProjectPath,
            node.ReferencedProjectPath,
            node.IsResolved,
            areChildrenLoaded: !shouldDeferChildren)
        {
            IsStartupProject = node.Kind == SolutionTreeNodeKind.Project &&
                               string.Equals(node.Path, _workspace.Current.StartupProjectPath, StringComparison.OrdinalIgnoreCase),
        };

        if (!shouldDeferChildren)
        {
            foreach (var child in node.Children)
                item.Children.Add(CreateTreeItem(child, deferProjectChildren));
        }

        return item;
    }

    private async Task LoadProjectChildrenAsync(SolutionTreeItem item, CancellationToken cancellationToken)
    {
        var solutionPath = _workspace.Current.CurrentSolutionPath;
        if (string.IsNullOrWhiteSpace(solutionPath) || string.IsNullOrWhiteSpace(item.Path))
            return;

        var projectCts = ResetProjectLoad(item.Path);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, projectCts.Token);

        try
        {
            var projectNode = await _solutionLoader.LoadProjectAsync(solutionPath, item.Path, linkedCts.Token);
            if (linkedCts.Token.IsCancellationRequested)
                return;

            item.Children.Clear();
            foreach (var child in projectNode.Children)
                item.Children.Add(CreateTreeItem(child, deferProjectChildren: false));

            item.MarkChildrenLoaded();
            RestoreExpandedTreeState(item.Children, _expandedTreeKeys);
            _projectAssociations.UpdateProject(solutionPath, projectNode);
            UpdateActiveItem(_workspace.Current.TabSession.ActiveDocument?.Path);
            UpdateStartupProject(_workspace.Current.StartupProjectPath);
        }
        catch (OperationCanceledException) { }
        catch (SolutionWorkspaceLoadException ex)
        {
            item.ResetPlaceholder();
            _notifications.ShowError("Unable to load project", ex.Reason);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            item.ResetPlaceholder();
            _notifications.ShowError("Unable to load project", "The project could not be read.");
        }
        finally
        {
            ClearProjectLoad(item.Path, projectCts);
        }
    }

    private ICommand? CreateOpenCommand(SolutionTreeNode node) => node.Kind switch
    {
        SolutionTreeNodeKind.File when node.Path is not null => new RelayCommand(() => OpenPath(node.Path)),
        SolutionTreeNodeKind.Project when node.Path is not null => new RelayCommand(() => OpenPath(node.Path)),
        _ => null,
    };

    private ICommand? CreateNewFileCommand(SolutionTreeNode node) => node.Kind switch
    {
        SolutionTreeNodeKind.Project when node.Path is not null => new AsyncRelayCommand(() => CreateFileAsync(node)),
        SolutionTreeNodeKind.Folder when node.Path is not null => new AsyncRelayCommand(() => CreateFileAsync(node)),
        _ => null,
    };

    private ICommand? CreateNewFolderCommand(SolutionTreeNode node) => node.Kind switch
    {
        SolutionTreeNodeKind.Project when node.Path is not null => new AsyncRelayCommand(() => CreatePhysicalFolderAsync(node)),
        SolutionTreeNodeKind.Folder when node.Path is not null => new AsyncRelayCommand(() => CreatePhysicalFolderAsync(node)),
        SolutionTreeNodeKind.SolutionFolder => new AsyncRelayCommand(() => CreateSolutionSubfolderAsync(node)),
        _ => null,
    };

    private ICommand? CreateCopyCommand(SolutionTreeNode node) => node.Kind switch
    {
        SolutionTreeNodeKind.File when node.Path is not null => new RelayCommand(() => _clipboard.Copy(node.Path)),
        SolutionTreeNodeKind.Project when node.Path is not null => new RelayCommand(() => _clipboard.Copy(node.Path)),
        _ => null,
    };

    private ICommand? CreatePasteCommand(SolutionTreeNode node) => node.Kind switch
    {
        SolutionTreeNodeKind.Solution when node.Path is not null => new AsyncRelayCommand(() => PasteAsync(node)),
        SolutionTreeNodeKind.Project when node.Path is not null => new AsyncRelayCommand(() => PasteAsync(node)),
        _ => null,
    };

    private ICommand? CreateDeleteCommand(SolutionTreeNode node) => node.Kind switch
    {
        SolutionTreeNodeKind.File when node.Path is not null => new AsyncRelayCommand(() => DeleteAsync(node, isDirectory: false)),
        SolutionTreeNodeKind.Project when node.Path is not null => new AsyncRelayCommand(() => DeleteAsync(node, isDirectory: false)),
        SolutionTreeNodeKind.Folder when node.Path is not null => new AsyncRelayCommand(() => DeletePhysicalFolderAsync(node)),
        _ => null,
    };

    private ICommand? CreateLoadSolutionCommand(SolutionTreeNode node) =>
        node.Kind == SolutionTreeNodeKind.File && node.Path is not null && IsSolutionPath(node.Path)
            ? new RelayCommand(() => _eventBus.Publish(new OpenSolutionRequestedEvent(node.Path)))
            : null;

    private ICommand? CreateCloseSolutionCommand(SolutionTreeNode node) =>
        node.Kind == SolutionTreeNodeKind.Solution && node.Path is not null
            ? new RelayCommand(() => CloseSolution(node.Path))
            : null;

    private ICommand? CreateManageNuGetPackagesCommand(SolutionTreeNode node)
    {
        var solutionPath = node.Kind switch
        {
            SolutionTreeNodeKind.Solution when node.Path is not null => node.Path,
            SolutionTreeNodeKind.Project when node.Path is not null => _workspace.Current.CurrentSolutionPath,
            SolutionTreeNodeKind.File when node.Path is not null && IsProjectPath(node.Path) => _workspace.Current.CurrentSolutionPath,
            _ => null,
        };

        return string.IsNullOrWhiteSpace(solutionPath)
            ? null
            : new RelayCommand(() => _eventBus.Publish(new ManageNuGetPackagesRequestedEvent(solutionPath)));
    }

    private ICommand? CreateBuildCommand(SolutionTreeNode node) => node.Kind switch
    {
        SolutionTreeNodeKind.Solution => CreateTerminalCommand(() => _eventBus.Publish(new BuildWorkspaceRequestedEvent())),
        SolutionTreeNodeKind.Project when node.Path is not null => CreateTerminalCommand(() => _eventBus.Publish(new BuildProjectRequestedEvent(node.Path))),
        _ => null,
    };

    private ICommand? CreateRestoreCommand(SolutionTreeNode node) => node.Kind switch
    {
        SolutionTreeNodeKind.Solution => CreateTerminalCommand(() => _eventBus.Publish(new RestoreWorkspaceRequestedEvent())),
        SolutionTreeNodeKind.Project when node.Path is not null => CreateTerminalCommand(() => _eventBus.Publish(new RestoreProjectRequestedEvent(node.Path))),
        _ => null,
    };

    private ICommand? CreateCleanCommand(SolutionTreeNode node) => node.Kind switch
    {
        SolutionTreeNodeKind.Solution => CreateTerminalCommand(() => _eventBus.Publish(new CleanWorkspaceRequestedEvent())),
        SolutionTreeNodeKind.Project when node.Path is not null => CreateTerminalCommand(() => _eventBus.Publish(new CleanProjectRequestedEvent(node.Path))),
        _ => null,
    };

    private ICommand? CreatePublishCommand(SolutionTreeNode node) =>
        node.Kind == SolutionTreeNodeKind.Solution
            ? new RelayCommand(() => _eventBus.Publish(new PublishProjectRequestedEvent()))
            : null;

    private ICommand? CreateRunCommand(SolutionTreeNode node) =>
        node.Kind == SolutionTreeNodeKind.Project && node.Path is not null
            ? CreateTerminalCommand(() => _eventBus.Publish(new RunSpecificProjectRequestedEvent(node.Path)))
            : null;

    private ICommand? CreateTestCommand(SolutionTreeNode node) =>
        node.Kind == SolutionTreeNodeKind.Project && node.Path is not null
            ? CreateTerminalCommand(() => _eventBus.Publish(new TestProjectRequestedEvent(node.Path)))
            : null;

    private ICommand? CreateSetStartupProjectCommand(SolutionTreeNode node) =>
        node.Kind == SolutionTreeNodeKind.Project && node.Path is not null
            ? new AsyncRelayCommand(token => _setStartupProjectHandler.HandleAsync(new SetStartupProjectCommand(node.Path), token))
            : null;

    private ICommand? CreateAddProjectReferenceCommand(SolutionTreeNode node) =>
        node.Kind == SolutionTreeNodeKind.Project && node.Path is not null
            ? new AsyncRelayCommand(token => AddProjectReferenceAsync(node.Path, token))
            : null;

    private ICommand? CreateRemoveProjectReferenceCommand(SolutionTreeNode node) =>
        node.Kind == SolutionTreeNodeKind.ProjectReference &&
        node.ProjectPath is not null &&
        node.ReferencedProjectPath is not null
            ? new AsyncRelayCommand(token => RemoveProjectReferenceAsync(node.ProjectPath, node.ReferencedProjectPath, node.Name, token))
            : null;

    private ICommand CreateTerminalCommand(Action execute) =>
        new RelayCommand(() =>
        {
            _eventBus.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
            execute();
        });

    private async Task CreatePhysicalFolderAsync(SolutionTreeNode node)
    {
        var targetDirectory = GetTargetDirectory(node);
        var projectPath = GetProjectPath(node);
        if (targetDirectory is null || projectPath is null)
            return;

        try
        {
            var folderName = await _fileDialogs.PromptForNameAsync("New Folder", "Folder name");
            if (string.IsNullOrWhiteSpace(folderName))
                return;

            EnsureValidFileSystemName(folderName);

            var targetPath = Path.Combine(targetDirectory, folderName);
            if (Directory.Exists(targetPath) || File.Exists(targetPath))
            {
                _notifications.ShowWarning("Create folder", "A file or folder with that name already exists.");
                return;
            }

            await _solutionStructure.CreatePhysicalFolderAsync(projectPath, targetDirectory, folderName);
            await RefreshAfterDirectoryMutationAsync(projectPath, targetDirectory);
        }
        catch (Exception ex)
        {
            _notifications.ShowError("Unable to create folder", ex.Message);
        }
    }

    private async Task CreateSolutionSubfolderAsync(SolutionTreeNode node)
    {
        var solutionPath = _workspace.Current.CurrentSolutionPath;
        if (string.IsNullOrWhiteSpace(solutionPath))
            return;

        try
        {
            var folderName = await _fileDialogs.PromptForNameAsync("New Solution Folder", "Folder name");
            if (string.IsNullOrWhiteSpace(folderName))
                return;

            EnsureValidFileSystemName(folderName);

            await _solutionStructure.CreateSolutionFolderAsync(solutionPath, node.Name, folderName);
            await ReloadCurrentSolutionAsync();
        }
        catch (Exception ex)
        {
            _notifications.ShowError("Unable to create solution folder", ex.Message);
        }
    }

    private async Task CreateFileAsync(SolutionTreeNode node)
    {
        var targetDirectory = GetTargetDirectory(node);
        var projectPath = GetProjectPath(node);
        if (targetDirectory is null)
        {
            return;
        }

        if (projectPath is null)
        {
            return;
        }

        try
        {
            var creation = await _creationDialogs.ShowCreateFileDialogAsync();
            if (creation is null)
            {
                return;
            }

            var fileName = string.IsNullOrWhiteSpace(creation.Name) ? null : creation.Name.Trim();
            if (fileName is null)
            {
                return;
            }

            EnsureValidFileSystemName(fileName);

            var targetPath = Path.Combine(targetDirectory, fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? fileName : $"{fileName}.cs");
            if (Directory.Exists(targetPath) || File.Exists(targetPath))
            {
                _notifications.ShowWarning("Create file", "A file or folder with that name already exists.");
                return;
            }

            var content = SolutionFileTemplateBuilder.Build(projectPath, targetDirectory, Path.GetFileName(targetPath), creation.Kind);
            _fileService.WriteText(targetPath, content);
            await RefreshAfterDirectoryMutationAsync(projectPath, targetDirectory);
            _eventBus.Publish(new OpenFileRequestedEvent(targetPath));
        }
        catch (Exception ex)
        {
            _notifications.ShowError("Unable to create file", ex.Message);
        }
    }

    private static string? GetTargetDirectory(SolutionTreeNode node)
    {
        return node.Kind switch
        {
            SolutionTreeNodeKind.Project when node.Path is not null => Path.GetDirectoryName(node.Path),
            SolutionTreeNodeKind.Folder when node.Path is not null => node.Path,
            _ => null,
        };
    }

    private static string? GetProjectPath(SolutionTreeNode node)
    {
        return node.Kind switch
        {
            SolutionTreeNodeKind.Project => node.Path,
            SolutionTreeNodeKind.Folder => node.ProjectPath,
            _ => null,
        };
    }

    private static void EnsureValidFileSystemName(string name)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException("The name contains invalid characters.");
        }
    }

    private void ActivateItem(SolutionTreeItem item)
    {
        try
        {
            item.OpenCommand?.Execute(null);
        }
        catch (Exception ex) when (!string.IsNullOrWhiteSpace(item.Path) &&
                                   OpenFileFailureNotification.TryShow(_notifications, item.Path, ex))
        {
        }
    }

    private void OpenPath(string path)
    {
        try
        {
            _eventBus.Publish(new OpenFileRequestedEvent(path));
        }
        catch (Exception ex) when (OpenFileFailureNotification.TryShow(_notifications, path, ex))
        {
        }
    }

    private async Task PasteAsync(SolutionTreeNode node)
    {
        try
        {
            var destinationDirectory = GetPasteDestinationDirectory(node);
            if (destinationDirectory is null)
                return;

            await _clipboard.PasteAsync(destinationDirectory);

            var projectPath = GetProjectPath(node);
            if (projectPath is not null)
                await RefreshAfterDirectoryMutationAsync(projectPath, destinationDirectory);
        }
        catch (Exception ex)
        {
            _notifications.ShowError("Unable to paste", ex.Message);
        }
    }

    private static string? GetPasteDestinationDirectory(SolutionTreeNode node)
    {
        return node.Kind switch
        {
            SolutionTreeNodeKind.Solution when node.Path is not null => Path.GetDirectoryName(node.Path) ?? node.Path,
            SolutionTreeNodeKind.Project when node.Path is not null => Path.GetDirectoryName(node.Path) ?? node.Path,
            SolutionTreeNodeKind.Folder when node.Path is not null => node.Path,
            _ => null,
        };
    }

    private async Task DeleteAsync(SolutionTreeNode node, bool isDirectory)
    {
        try
        {
            if (node.Path is null)
                return;

            var confirmed = await _fileDialogs.ConfirmDeleteAsync(node.Path, isDirectory);
            if (!confirmed)
                return;

            _fileService.Delete(node.Path, isDirectory);

            if (node.Kind == SolutionTreeNodeKind.Project)
                await ReloadCurrentSolutionAsync();
            else if (GetProjectPath(node) is { } projectPath)
                await RefreshAfterDirectoryMutationAsync(projectPath, Path.GetDirectoryName(node.Path));
        }
        catch (Exception ex)
        {
            _notifications.ShowError("Unable to delete", ex.Message);
        }
    }

    private async Task DeletePhysicalFolderAsync(SolutionTreeNode node)
    {
        if (node.Path is null)
            return;

        var projectPath = GetProjectPath(node);
        if (projectPath is null)
            return;

        try
        {
            var confirmed = await _fileDialogs.ConfirmDeleteAsync(node.Path, isDirectory: true);
            if (!confirmed)
                return;

            await _solutionStructure.RemovePhysicalFolderAsync(projectPath, node.Path);
            _fileService.Delete(node.Path, isDirectory: true);
            await RefreshAfterDirectoryMutationAsync(projectPath, Path.GetDirectoryName(node.Path));
        }
        catch (Exception ex)
        {
            _notifications.ShowError("Unable to delete folder", ex.Message);
        }
    }

    private void CloseSolution(string solutionPath)
    {
        var folder = Path.GetDirectoryName(solutionPath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        _eventBus.Publish(new OpenFolderRequestedEvent(folder));
    }

    private static bool IsSolutionPath(string path) =>
        string.Equals(Path.GetExtension(path), ".sln", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(path), ".slnx", StringComparison.OrdinalIgnoreCase);

    private static bool IsProjectPath(string path) =>
        Path.GetExtension(path).EndsWith("proj", StringComparison.OrdinalIgnoreCase);

    private async Task AddProjectReferenceAsync(string projectPath, CancellationToken cancellationToken)
    {
        if (_loadedSnapshot is null) return;

        try
        {
            var candidates = await _projectReferences.GetReferenceCandidatesAsync(projectPath, _loadedSnapshot, cancellationToken);
            if (candidates.Count == 0)
            {
                _notifications.ShowWarning("Add Reference", "No eligible projects are available.");
                return;
            }

            var selectedPaths = await _referenceDialogs.ShowAddReferenceDialogAsync(candidates, cancellationToken);
            if (selectedPaths.Count == 0) return;

            await _addProjectReferencesHandler.HandleAsync(new AddProjectReferencesCommand(projectPath, selectedPaths), cancellationToken);
            await RefreshProjectBranchAsync(projectPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Xml.XmlException)
        {
            _notifications.ShowError("Unable to add reference", ex.Message);
        }
    }

    private async Task RemoveProjectReferenceAsync(
        string projectPath,
        string referencedProjectPath,
        string referenceName,
        CancellationToken cancellationToken)
    {
        try
        {
            var confirmed = await _referenceDialogs.ConfirmRemoveProjectReferenceAsync(referenceName, cancellationToken);
            if (!confirmed) return;

            await _removeProjectReferenceHandler.HandleAsync(
                new RemoveProjectReferenceCommand(projectPath, referencedProjectPath),
                cancellationToken);
            await RefreshProjectBranchAsync(projectPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Xml.XmlException)
        {
            _notifications.ShowError("Unable to remove reference", ex.Message);
        }
    }

    public Task ReloadCurrentSolutionAsync()
    {
        var solutionPath = _workspace.Current.CurrentSolutionPath;
        if (string.IsNullOrWhiteSpace(solutionPath))
            return Task.CompletedTask;

        _loadedSolutionPath = solutionPath;
        return LoadAsync(solutionPath);
    }

    private async Task RefreshProjectBranchAsync(string projectPath)
    {
        var solutionPath = _workspace.Current.CurrentSolutionPath;
        if (string.IsNullOrWhiteSpace(solutionPath) || string.IsNullOrWhiteSpace(projectPath))
            return;

        var projectItem = FindProjectItem(projectPath);
        if (projectItem is null)
            return;

        var projectKey = BuildTreeKey(string.Empty, projectItem);
        var expandedKeys = new HashSet<string>(StringComparer.Ordinal);
        CaptureExpandedTreeState(projectItem.Children, projectKey, expandedKeys);

        try
        {
            var projectNode = await _solutionLoader.LoadProjectAsync(solutionPath, projectPath);
            _projectAssociations.UpdateProject(solutionPath, projectNode);

            projectItem.Children.Clear();
            foreach (var child in projectNode.Children)
                projectItem.Children.Add(CreateTreeItem(child, deferProjectChildren: false));

            projectItem.MarkChildrenLoaded();
            RestoreExpandedTreeState(projectItem.Children, expandedKeys, projectKey);
            UpdateActiveItem(_workspace.Current.TabSession.ActiveDocument?.Path);
            UpdateStartupProject(_workspace.Current.StartupProjectPath);
        }
        catch (Exception ex)
        {
            _notifications.ShowError("Unable to refresh project", ex.Message);
        }
    }

    private async Task RefreshAfterDirectoryMutationAsync(string projectPath, string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return;

        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            await RefreshProjectBranchAsync(projectPath);
            return;
        }

        var folderItem = FindFolderItem(directoryPath);
        if (folderItem is null)
        {
            await RefreshProjectBranchAsync(projectPath);
            return;
        }

        await RefreshFolderBranchAsync(folderItem, projectPath);
    }

    private async Task RefreshFolderBranchAsync(SolutionTreeItem folderItem, string projectPath)
    {
        if (folderItem.Path is null)
            return;

        var folderKey = BuildTreeKey(string.Empty, folderItem);
        var expandedKeys = new HashSet<string>(StringComparer.Ordinal);
        CaptureExpandedTreeState(folderItem.Children, folderKey, expandedKeys);

        try
        {
            var folderNode = BuildFilesystemFolderNode(folderItem.Path, projectPath);

            folderItem.Children.Clear();
            foreach (var child in folderNode.Children)
                folderItem.Children.Add(CreateTreeItem(child, deferProjectChildren: false));

            folderItem.MarkChildrenLoaded();
            RestoreExpandedTreeState(folderItem.Children, expandedKeys, folderKey);
            UpdateActiveItem(_workspace.Current.TabSession.ActiveDocument?.Path);
            UpdateStartupProject(_workspace.Current.StartupProjectPath);
        }
        catch (Exception ex)
        {
            _notifications.ShowError("Unable to refresh folder", ex.Message);
            await RefreshProjectBranchAsync(projectPath);
        }
    }

    private CancellationTokenSource ResetProjectLoad(string projectPath)
    {
        if (_projectLoadCtsByPath.Remove(projectPath, out var existingCts))
        {
            existingCts.Cancel();
        }

        var cts = new CancellationTokenSource();
        _projectLoadCtsByPath[projectPath] = cts;
        return cts;
    }

    private void ClearProjectLoad(string projectPath, CancellationTokenSource cts)
    {
        if (_projectLoadCtsByPath.TryGetValue(projectPath, out var current) && ReferenceEquals(current, cts))
            _projectLoadCtsByPath.Remove(projectPath);

        cts.Dispose();
    }

    private void CancelProjectLoads()
    {
        foreach (var cts in _projectLoadCtsByPath.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _projectLoadCtsByPath.Clear();
    }

    private void UpdateActiveItem(string? activePath) =>
        UpdateActiveItemRecursive(RootItems, activePath);

    private void UpdateStartupProject(string? startupProjectPath) =>
        UpdateStartupProjectRecursive(RootItems, startupProjectPath);

    private static void UpdateStartupProjectRecursive(
        System.Collections.Generic.IEnumerable<SolutionTreeItem> items,
        string? startupProjectPath)
    {
        foreach (var item in items)
        {
            item.IsStartupProject = item.IsProject &&
                                    string.Equals(item.Path, startupProjectPath, StringComparison.OrdinalIgnoreCase);
            UpdateStartupProjectRecursive(item.Children, startupProjectPath);
        }
    }

    private static void UpdateActiveItemRecursive(
        System.Collections.Generic.IEnumerable<SolutionTreeItem> items,
        string? activePath)
    {
        foreach (var item in items)
        {
            item.IsActive = item.IsFile &&
                            string.Equals(item.Path, activePath, StringComparison.OrdinalIgnoreCase);
            UpdateActiveItemRecursive(item.Children, activePath);
        }
    }

    private HashSet<string> CaptureExpandedTreeState()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        CaptureExpandedTreeState(RootItems, string.Empty, keys);
        return keys;
    }

    private static void CaptureExpandedTreeState(
        System.Collections.Generic.IEnumerable<SolutionTreeItem> items,
        string parentKey,
        ISet<string> keys)
    {
        foreach (var item in items)
        {
            var key = BuildTreeKey(parentKey, item);
            if (item.IsExpanded)
                keys.Add(key);

            CaptureExpandedTreeState(item.Children, key, keys);
        }
    }

    private static void RestoreExpandedTreeState(
        System.Collections.Generic.IEnumerable<SolutionTreeItem> items,
        ISet<string>? keys,
        string parentKey = "")
    {
        if (keys is null)
            return;

        foreach (var item in items)
        {
            var key = BuildTreeKey(parentKey, item);
            item.IsExpanded = keys.Contains(key);
            RestoreExpandedTreeState(item.Children, keys, key);
        }
    }

    private static string BuildTreeKey(string parentKey, SolutionTreeItem item)
    {
        return string.Concat(
            parentKey,
            "\u001f",
            (int)item.Kind,
            "\u001f",
            item.Name,
            "\u001f",
            item.Path ?? string.Empty,
            "\u001f",
            item.ProjectPath ?? string.Empty,
            "\u001f",
            item.ReferencedProjectPath ?? string.Empty);
    }

    private SolutionTreeItem? FindProjectItem(string projectPath)
    {
        return FindProjectItemRecursive(RootItems, projectPath);
    }

    private static SolutionTreeItem? FindProjectItemRecursive(
        System.Collections.Generic.IEnumerable<SolutionTreeItem> items,
        string projectPath)
    {
        foreach (var item in items)
        {
            if (item.Kind == SolutionTreeNodeKind.Project &&
                string.Equals(item.Path, projectPath, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            var found = FindProjectItemRecursive(item.Children, projectPath);
            if (found is not null)
                return found;
        }

        return null;
    }

    private SolutionTreeItem? FindFolderItem(string folderPath)
    {
        return FindFolderItemRecursive(RootItems, folderPath);
    }

    private static SolutionTreeItem? FindFolderItemRecursive(
        System.Collections.Generic.IEnumerable<SolutionTreeItem> items,
        string folderPath)
    {
        foreach (var item in items)
        {
            if (item.Kind == SolutionTreeNodeKind.Folder &&
                string.Equals(item.Path, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            var found = FindFolderItemRecursive(item.Children, folderPath);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static SolutionTreeNode BuildFilesystemFolderNode(string folderPath, string projectPath)
    {
        var children = new List<SolutionTreeNode>();
        foreach (var childDirectory in GetVisibleDirectories(folderPath).OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            children.Add(BuildFilesystemFolderNode(childDirectory, projectPath));
        }

        foreach (var filePath in GetVisibleFiles(folderPath).OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            children.Add(new SolutionTreeNode(
                SolutionTreeNodeKind.File,
                Path.GetFileName(filePath),
                filePath,
                [],
                null,
                projectPath,
                null));
        }

        return new SolutionTreeNode(
            SolutionTreeNodeKind.Folder,
            Path.GetFileName(folderPath),
            folderPath,
            children,
            null,
            projectPath,
            null);
    }

    private static IEnumerable<string> GetVisibleDirectories(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return [];

        try
        {
            return Directory.EnumerateDirectories(folderPath, "*", SearchOption.TopDirectoryOnly)
                .Where(IsVisibleDirectory)
                .Select(Path.GetFullPath)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> GetVisibleFiles(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return [];

        try
        {
            return Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
                .Where(IsVisibleFile)
                .Select(Path.GetFullPath)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsVisibleFile(string path)
    {
        return File.Exists(path) && !IsHiddenPath(path) && !HasHiddenAttributes(path);
    }

    private static bool IsVisibleDirectory(string path)
    {
        return Directory.Exists(path) && !IsHiddenPath(path) && !HasHiddenAttributes(path);
    }

    private static bool IsHiddenPath(string path)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   .Any(segment => HiddenPathSegments.Contains(segment) ||
                                   (segment.Length > 1 && segment.StartsWith(".", StringComparison.Ordinal)));
    }

    private static bool HasHiddenAttributes(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Hidden) == FileAttributes.Hidden;
        }
        catch
        {
            return true;
        }
    }

    private static readonly HashSet<string> HiddenPathSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".hg",
        ".svn",
        ".vs",
        "bin",
        "debug",
        "obj",
        "release",
        "testresults",
    };
}
