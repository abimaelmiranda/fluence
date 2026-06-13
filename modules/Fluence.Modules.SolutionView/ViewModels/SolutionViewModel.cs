using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Commands;
using Fluence.Core.Modules;
using Fluence.Core.Ports;
using Fluence.Core.ViewModels;
using Fluence.Core.Workspace;

namespace Fluence.Modules.SolutionView.ViewModels;

public sealed partial class SolutionViewModel : ViewModelBase
{
    private readonly IWorkspaceContext _workspace;
    private readonly ISolutionWorkspaceLoader _solutionLoader;
    private readonly ICommandHandler<AddProjectReferencesCommand> _addProjectReferencesHandler;
    private readonly ICommandHandler<RemoveProjectReferenceCommand> _removeProjectReferenceHandler;
    private readonly ICommandHandler<SetStartupProjectCommand> _setStartupProjectHandler;
    private readonly IProjectReferenceService _projectReferences;
    private readonly IProjectReferenceDialogService _referenceDialogs;
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
        ICommandHandler<AddProjectReferencesCommand> addProjectReferencesHandler,
        ICommandHandler<RemoveProjectReferenceCommand> removeProjectReferenceHandler,
        ICommandHandler<SetStartupProjectCommand> setStartupProjectHandler,
        IProjectReferenceService projectReferences,
        IProjectReferenceDialogService referenceDialogs,
        IUserNotificationService notifications,
        IFileClipboardService clipboard,
        IFileOperationDialogService fileDialogs,
        IFileService fileService,
        IShellEventBus eventBus,
        SolutionProjectAssociationService projectAssociations)
    {
        _workspace = workspace;
        _solutionLoader = solutionLoader;
        _addProjectReferencesHandler = addProjectReferencesHandler;
        _removeProjectReferenceHandler = removeProjectReferenceHandler;
        _setStartupProjectHandler = setStartupProjectHandler;
        _projectReferences = projectReferences;
        _referenceDialogs = referenceDialogs;
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
            var snapshot = await _solutionLoader.LoadAsync(solutionPath, cancellationToken);
            _loadedSnapshot = snapshot;
            _projectAssociations.Update(snapshot);
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
            CreateCopyCommand(node),
            CreatePasteCommand(node),
            CreateDeleteCommand(node),
            CreateLoadSolutionCommand(node),
            CreateCloseSolutionCommand(node),
            CreateManageNuGetPackagesCommand(node),
            CreateBuildCommand(node),
            CreateRestoreCommand(node),
            CreateCleanCommand(node),
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
