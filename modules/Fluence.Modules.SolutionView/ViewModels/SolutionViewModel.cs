using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.File;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Services.File;
using Fluence.Core.ViewModels;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.SolutionView.Abstractions;
using Fluence.Modules.SolutionView.Commands;
using Fluence.Modules.SolutionView.Commands.AddProjectReference;
using Fluence.Modules.SolutionView.Commands.RemoveProjectReference;
using Fluence.Modules.SolutionView.Commands.SetStartupProject;
using Fluence.Modules.SolutionView.Exceptions;
using Fluence.Modules.SolutionView.Models;
using Fluence.Modules.SolutionView.Models.Enums;
using Fluence.Modules.SolutionView.Services;

namespace Fluence.Modules.SolutionView.ViewModels;

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
    private string? _loadedSolutionPath;
    private SolutionWorkspaceSnapshot? _loadedSnapshot;

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
        var solutionPath = _workspace.Current.CurrentSolutionPath;
        if (_workspace.Current.Mode != WorkspaceMode.Solution || string.IsNullOrWhiteSpace(solutionPath))
        {
            _loadedSolutionPath = null;
            _loadedSnapshot = null;
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
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var cancellationToken = _loadCts.Token;

        IsLoading = true;
        ErrorMessage = null;
        RootItems.Clear();
        _projectAssociations.Clear(solutionPath);

        try
        {
            // When cache is cold, show the structural skeleton immediately so the tree
            // appears in < 100ms while Buildalyzer runs in background.
            if (!_solutionLoader.HasValidCache(solutionPath))
            {
                var skeleton = await _solutionLoader.LoadStructuralAsync(solutionPath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                RootItems.Add(CreateTreeItem(skeleton.Root));
                IsLoading = false;
            }

            var snapshot = await _solutionLoader.LoadAsync(solutionPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _loadedSnapshot = snapshot;
            _projectAssociations.Update(snapshot);

            RootItems.Clear();
            RootItems.Add(CreateTreeItem(snapshot.Root));
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

    private SolutionTreeItem CreateTreeItem(SolutionTreeNode node)
    {
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
            node.ProjectPath,
            node.ReferencedProjectPath,
            node.IsResolved)
        {
            IsStartupProject = node.Kind == SolutionTreeNodeKind.Project &&
                               string.Equals(node.Path, _workspace.Current.StartupProjectPath, StringComparison.OrdinalIgnoreCase),
        };

        foreach (var child in node.Children)
            item.Children.Add(CreateTreeItem(child));

        return item;
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
        SolutionTreeNodeKind.Solution when node.Path is not null => new AsyncRelayCommand(() => PasteAsync(Path.GetDirectoryName(node.Path) ?? node.Path)),
        SolutionTreeNodeKind.Project when node.Path is not null => new AsyncRelayCommand(() => PasteAsync(Path.GetDirectoryName(node.Path) ?? node.Path)),
        _ => null,
    };

    private ICommand? CreateDeleteCommand(SolutionTreeNode node) => node.Kind switch
    {
        SolutionTreeNodeKind.File when node.Path is not null => new AsyncRelayCommand(() => DeleteAsync(node.Path, isDirectory: false)),
        SolutionTreeNodeKind.Project when node.Path is not null => new AsyncRelayCommand(() => DeleteAsync(node.Path, isDirectory: false)),
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
            _eventBus.Publish(new ExpandPanelEvent("Terminal"));
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
            await ReloadCurrentSolutionAsync();
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
            await ReloadCurrentSolutionAsync();
            _eventBus.Publish(new OpenFileRequestedEvent(targetPath));
        }
        catch (Exception ex)
        {
            _notifications.ShowError("Unable to create file", ex.Message);
        }
    }

    private string? GetTargetDirectory(SolutionTreeNode node)
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

    private async Task PasteAsync(string destinationDirectory)
    {
        try
        {
            await _clipboard.PasteAsync(destinationDirectory);
            await ReloadCurrentSolutionAsync();
        }
        catch (Exception ex)
        {
            _notifications.ShowError("Unable to paste", ex.Message);
        }
    }

    private async Task DeleteAsync(string path, bool isDirectory)
    {
        try
        {
            var confirmed = await _fileDialogs.ConfirmDeleteAsync(path, isDirectory);
            if (!confirmed)
                return;

            _fileService.Delete(path, isDirectory);
            await ReloadCurrentSolutionAsync();
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
            await ReloadCurrentSolutionAsync();
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
            await ReloadCurrentSolutionAsync();
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
            await ReloadCurrentSolutionAsync();
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
}
