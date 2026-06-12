using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Application.DotnetCli;
using Fluence.Application.Workspace;
using Fluence.Core.Commands;
using Fluence.Core.Workspace;
using Fluence.Desktop.Services;

namespace Fluence.Desktop.ViewModels;

public sealed partial class SolutionViewModel : ViewModelBase
{
    private readonly IWorkspaceContext _workspace;
    private readonly ISolutionWorkspaceLoader _solutionLoader;
    private readonly ICommandHandler<OpenFileWorkspaceCommand> _openFileHandler;
    private readonly ICommandHandler<BuildWorkspaceCommand> _buildWorkspaceHandler;
    private readonly ICommandHandler<RestoreWorkspaceCommand> _restoreWorkspaceHandler;
    private readonly ICommandHandler<CleanWorkspaceCommand> _cleanWorkspaceHandler;
    private readonly ICommandHandler<BuildProjectCommand> _buildProjectHandler;
    private readonly ICommandHandler<RestoreProjectCommand> _restoreProjectHandler;
    private readonly ICommandHandler<CleanProjectCommand> _cleanProjectHandler;
    private readonly ICommandHandler<RunSpecificProjectCommand> _runProjectHandler;
    private readonly ICommandHandler<TestProjectCommand> _testProjectHandler;
    private readonly ICommandHandler<AddProjectReferencesCommand> _addProjectReferencesHandler;
    private readonly ICommandHandler<RemoveProjectReferenceCommand> _removeProjectReferenceHandler;
    private readonly ICommandHandler<SetStartupProjectCommand> _setStartupProjectHandler;
    private readonly IProjectReferenceService _projectReferences;
    private readonly IProjectReferenceDialogService _referenceDialogs;
    private readonly IUserNotificationService _notifications;
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
        ICommandHandler<OpenFileWorkspaceCommand> openFileHandler,
        ICommandHandler<BuildWorkspaceCommand> buildWorkspaceHandler,
        ICommandHandler<RestoreWorkspaceCommand> restoreWorkspaceHandler,
        ICommandHandler<CleanWorkspaceCommand> cleanWorkspaceHandler,
        ICommandHandler<BuildProjectCommand> buildProjectHandler,
        ICommandHandler<RestoreProjectCommand> restoreProjectHandler,
        ICommandHandler<CleanProjectCommand> cleanProjectHandler,
        ICommandHandler<RunSpecificProjectCommand> runProjectHandler,
        ICommandHandler<TestProjectCommand> testProjectHandler,
        ICommandHandler<AddProjectReferencesCommand> addProjectReferencesHandler,
        ICommandHandler<RemoveProjectReferenceCommand> removeProjectReferenceHandler,
        ICommandHandler<SetStartupProjectCommand> setStartupProjectHandler,
        IProjectReferenceService projectReferences,
        IProjectReferenceDialogService referenceDialogs,
        IUserNotificationService notifications)
    {
        _workspace = workspace;
        _solutionLoader = solutionLoader;
        _openFileHandler = openFileHandler;
        _buildWorkspaceHandler = buildWorkspaceHandler;
        _restoreWorkspaceHandler = restoreWorkspaceHandler;
        _cleanWorkspaceHandler = cleanWorkspaceHandler;
        _buildProjectHandler = buildProjectHandler;
        _restoreProjectHandler = restoreProjectHandler;
        _cleanProjectHandler = cleanProjectHandler;
        _runProjectHandler = runProjectHandler;
        _testProjectHandler = testProjectHandler;
        _addProjectReferencesHandler = addProjectReferencesHandler;
        _removeProjectReferenceHandler = removeProjectReferenceHandler;
        _setStartupProjectHandler = setStartupProjectHandler;
        _projectReferences = projectReferences;
        _referenceDialogs = referenceDialogs;
        _notifications = notifications;
        _workspace.Changed += OnWorkspaceChanged;
        RefreshForWorkspace();
    }

    public event EventHandler? TerminalExpandRequested;

    public ObservableCollection<SolutionTreeItem> RootItems { get; } = [];

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

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
            RootItems.Clear();
            ErrorMessage = null;
            return;
        }

        if (string.Equals(_loadedSolutionPath, solutionPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

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

        try
        {
            var snapshot = await _solutionLoader.LoadAsync(solutionPath, cancellationToken);
            _loadedSnapshot = snapshot;
            RootItems.Add(CreateTreeItem(snapshot.Root));
            UpdateActiveItem(_workspace.Current.TabSession.ActiveDocument?.Path);
            UpdateStartupProject(_workspace.Current.StartupProjectPath);
        }
        catch (OperationCanceledException)
        {
        }
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
        {
            item.Children.Add(CreateTreeItem(child));
        }

        return item;
    }

    private ICommand? CreateBuildCommand(SolutionTreeNode node)
    {
        return node.Kind switch
        {
            SolutionTreeNodeKind.Solution => CreateTerminalCommand(token => _buildWorkspaceHandler.HandleAsync(new BuildWorkspaceCommand(), token)),
            SolutionTreeNodeKind.Project when node.Path is not null => CreateTerminalCommand(token => _buildProjectHandler.HandleAsync(new BuildProjectCommand(node.Path), token)),
            _ => null,
        };
    }

    private ICommand? CreateRestoreCommand(SolutionTreeNode node)
    {
        return node.Kind switch
        {
            SolutionTreeNodeKind.Solution => CreateTerminalCommand(token => _restoreWorkspaceHandler.HandleAsync(new RestoreWorkspaceCommand(), token)),
            SolutionTreeNodeKind.Project when node.Path is not null => CreateTerminalCommand(token => _restoreProjectHandler.HandleAsync(new RestoreProjectCommand(node.Path), token)),
            _ => null,
        };
    }

    private ICommand? CreateCleanCommand(SolutionTreeNode node)
    {
        return node.Kind switch
        {
            SolutionTreeNodeKind.Solution => CreateTerminalCommand(token => _cleanWorkspaceHandler.HandleAsync(new CleanWorkspaceCommand(), token)),
            SolutionTreeNodeKind.Project when node.Path is not null => CreateTerminalCommand(token => _cleanProjectHandler.HandleAsync(new CleanProjectCommand(node.Path), token)),
            _ => null,
        };
    }

    private ICommand? CreateRunCommand(SolutionTreeNode node)
    {
        return node.Kind == SolutionTreeNodeKind.Project && node.Path is not null
            ? CreateTerminalCommand(token => _runProjectHandler.HandleAsync(new RunSpecificProjectCommand(node.Path), token))
            : null;
    }

    private ICommand? CreateTestCommand(SolutionTreeNode node)
    {
        return node.Kind == SolutionTreeNodeKind.Project && node.Path is not null
            ? CreateTerminalCommand(token => _testProjectHandler.HandleAsync(new TestProjectCommand(node.Path), token))
            : null;
    }

    private ICommand? CreateSetStartupProjectCommand(SolutionTreeNode node)
    {
        return node.Kind == SolutionTreeNodeKind.Project && node.Path is not null
            ? new AsyncRelayCommand(token => _setStartupProjectHandler.HandleAsync(new SetStartupProjectCommand(node.Path), token))
            : null;
    }

    private ICommand? CreateAddProjectReferenceCommand(SolutionTreeNode node)
    {
        return node.Kind == SolutionTreeNodeKind.Project && node.Path is not null
            ? new AsyncRelayCommand(token => AddProjectReferenceAsync(node.Path, token))
            : null;
    }

    private ICommand? CreateRemoveProjectReferenceCommand(SolutionTreeNode node)
    {
        return node.Kind == SolutionTreeNodeKind.ProjectReference &&
               node.ProjectPath is not null &&
               node.ReferencedProjectPath is not null
            ? new AsyncRelayCommand(token => RemoveProjectReferenceAsync(node.ProjectPath, node.ReferencedProjectPath, node.Name, token))
            : null;
    }

    private ICommand CreateTerminalCommand(Func<CancellationToken, Task> execute)
    {
        return new AsyncRelayCommand(async token =>
        {
            TerminalExpandRequested?.Invoke(this, EventArgs.Empty);
            await execute(token);
        });
    }

    private async void ActivateItem(SolutionTreeItem item)
    {
        if (!item.IsFile || string.IsNullOrWhiteSpace(item.Path))
        {
            return;
        }

        try
        {
            await _openFileHandler.HandleAsync(new OpenFileWorkspaceCommand(item.Path));
        }
        catch (Exception ex) when (OpenFileFailureNotification.TryShow(_notifications, item.Path, ex))
        {
        }
    }

    private async Task AddProjectReferenceAsync(string projectPath, CancellationToken cancellationToken)
    {
        if (_loadedSnapshot is null)
        {
            return;
        }

        try
        {
            var candidates = await _projectReferences.GetReferenceCandidatesAsync(projectPath, _loadedSnapshot, cancellationToken);
            if (candidates.Count == 0)
            {
                _notifications.ShowWarning("Add Reference", "No eligible projects are available.");
                return;
            }

            var selectedPaths = await _referenceDialogs.ShowAddReferenceDialogAsync(candidates, cancellationToken);
            if (selectedPaths.Count == 0)
            {
                return;
            }

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
            if (!confirmed)
            {
                return;
            }

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

    private Task ReloadCurrentSolutionAsync()
    {
        var solutionPath = _workspace.Current.CurrentSolutionPath;
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return Task.CompletedTask;
        }

        _loadedSolutionPath = solutionPath;
        return LoadAsync(solutionPath);
    }

    private void UpdateActiveItem(string? activePath)
    {
        UpdateActiveItemRecursive(RootItems, activePath);
    }

    private void UpdateStartupProject(string? startupProjectPath)
    {
        UpdateStartupProjectRecursive(RootItems, startupProjectPath);
    }

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
