using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Application.DotnetCli;
using Fluence.Application.Workspace;
using Fluence.Core.Commands;
using Fluence.Core.Workspace;
using Fluence.Desktop.Services;

namespace Fluence.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IWorkspaceContext _workspace;
    private readonly ICommandHandler<SaveActiveDocumentCommand> _saveActiveDocumentHandler;
    private readonly ICommandHandler<BuildWorkspaceCommand> _buildHandler;
    private readonly ICommandHandler<RunProjectCommand> _runHandler;
    private readonly ICommandHandler<TestWorkspaceCommand> _testHandler;
    private readonly ICommandHandler<RestoreWorkspaceCommand> _restoreHandler;
    private readonly ICommandHandler<CleanWorkspaceCommand> _cleanHandler;
    private readonly IUserNotificationService _notifications;

    [ObservableProperty]
    private WorkspaceMode _workspaceMode;

    [ObservableProperty]
    private bool _isTerminalExpanded;

    public MainWindowViewModel(
        IWorkspaceContext workspace,
        WelcomeViewModel welcome,
        EditorViewModel editor,
        FileExplorerViewModel fileExplorer,
        SolutionViewModel solution,
        TerminalViewModel terminal,
        ICommandHandler<SaveActiveDocumentCommand> saveActiveDocumentHandler,
        ICommandHandler<BuildWorkspaceCommand> buildHandler,
        ICommandHandler<RunProjectCommand> runHandler,
        ICommandHandler<TestWorkspaceCommand> testHandler,
        ICommandHandler<RestoreWorkspaceCommand> restoreHandler,
        ICommandHandler<CleanWorkspaceCommand> cleanHandler,
        IUserNotificationService notifications)
    {
        _workspace = workspace;
        _saveActiveDocumentHandler = saveActiveDocumentHandler;
        _buildHandler = buildHandler;
        _runHandler = runHandler;
        _testHandler = testHandler;
        _restoreHandler = restoreHandler;
        _cleanHandler = cleanHandler;
        _notifications = notifications;
        Welcome = welcome;
        Editor = editor;
        FileExplorer = fileExplorer;
        Solution = solution;
        Terminal = terminal;
        _workspaceMode = workspace.Current.Mode;
        _workspace.Changed += OnWorkspaceChanged;
        Solution.TerminalExpandRequested += OnTerminalExpandRequested;
    }

    public WelcomeViewModel Welcome { get; }

    public EditorViewModel Editor { get; }

    public FileExplorerViewModel FileExplorer { get; }

    public SolutionViewModel Solution { get; }

    public TerminalViewModel Terminal { get; }

    public bool IsWelcomeVisible => WorkspaceMode == WorkspaceMode.Empty;

    public bool IsWorkspaceVisible => WorkspaceMode != WorkspaceMode.Empty;

    public bool IsSidebarVisible => WorkspaceMode is WorkspaceMode.Folder or WorkspaceMode.Solution;

    public bool IsFolderMode => WorkspaceMode == WorkspaceMode.Folder;

    public bool IsSolutionMode => WorkspaceMode == WorkspaceMode.Solution;

    public bool HasActiveDocument => _workspace.Current.TabSession.ActiveDocument is not null;

    public bool HasNoActiveDocument => !HasActiveDocument;

    public IReadOnlyList<DocumentTabViewModel> OpenDocuments => _workspace.Current.TabSession.Documents
        .Select(document => new DocumentTabViewModel(
            document.Path,
            document.DisplayName,
            string.Equals(document.Path, ActiveDocumentPath, StringComparison.Ordinal),
            document.IsDirty,
            ActivateDocument,
            CloseDocument))
        .ToArray();

    public string? ActiveDocumentName => _workspace.Current.TabSession.ActiveDocument?.DisplayName;

    public string? ActiveDocumentPath => _workspace.Current.TabSession.ActiveDocument?.Path;

    public string WorkspaceTitle => WorkspaceMode switch
    {
        WorkspaceMode.FileOnly => ActiveDocumentName ?? "File Workspace",
        WorkspaceMode.Folder => Path.GetFileName(_workspace.Current.CurrentFolderPath) ?? "Folder Workspace",
        WorkspaceMode.Solution => Path.GetFileName(_workspace.Current.CurrentSolutionPath) ?? "Solution Workspace",
        _ => "Fluence IDE",
    };

    public string? WorkspacePath => WorkspaceMode switch
    {
        WorkspaceMode.FileOnly => _workspace.Current.CurrentFilePath,
        WorkspaceMode.Folder => _workspace.Current.CurrentFolderPath,
        WorkspaceMode.Solution => _workspace.Current.CurrentSolutionPath,
        _ => null,
    };

    public string EditorPlaceholder => WorkspaceMode switch
    {
        WorkspaceMode.FileOnly => "Open a text file to start editing",
        WorkspaceMode.Folder => "Open a file from the folder workspace",
        WorkspaceMode.Solution => "Open a file from the solution workspace",
        _ => "Editor",
    };

    public string SidebarPlaceholder => WorkspaceMode switch
    {
        WorkspaceMode.Folder => "File Explorer",
        WorkspaceMode.Solution => "Solution View",
        _ => "Sidebar",
    };

    public string SidebarDetail => WorkspaceMode switch
    {
        WorkspaceMode.Folder => _workspace.Current.CurrentFolderPath ?? "No folder opened",
        WorkspaceMode.Solution => _workspace.Current.CurrentSolutionPath ?? "No solution opened",
        _ => string.Empty,
    };

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        WorkspaceMode = _workspace.Current.Mode;
        OnPropertyChanged(nameof(IsWelcomeVisible));
        OnPropertyChanged(nameof(IsWorkspaceVisible));
        OnPropertyChanged(nameof(IsSidebarVisible));
        OnPropertyChanged(nameof(IsFolderMode));
        OnPropertyChanged(nameof(IsSolutionMode));
        BuildCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();
        TestCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasActiveDocument));
        OnPropertyChanged(nameof(HasNoActiveDocument));
        OnPropertyChanged(nameof(OpenDocuments));
        OnPropertyChanged(nameof(ActiveDocumentName));
        OnPropertyChanged(nameof(ActiveDocumentPath));
        OnPropertyChanged(nameof(WorkspaceTitle));
        OnPropertyChanged(nameof(WorkspacePath));
        OnPropertyChanged(nameof(EditorPlaceholder));
        OnPropertyChanged(nameof(SidebarPlaceholder));
        OnPropertyChanged(nameof(SidebarDetail));
    }

    private void OnTerminalExpandRequested(object? sender, EventArgs e)
    {
        IsTerminalExpanded = true;
    }

    [RelayCommand]
    private void ToggleTerminal()
    {
        IsTerminalExpanded = !IsTerminalExpanded;
    }

    [RelayCommand]
    private void TriggerCrash()
    {
        Dispatcher.UIThread.Post(() =>
        {
            throw new InvalidOperationException("Manual crash test triggered from the Debug menu.");
        });
    }

    [RelayCommand]
    private void SimulateSolutionLoadFailure()
    {
        _notifications.ShowError("Unable to load solution", "Buildalyzer could not open this solution.");
    }

    [RelayCommand]
    private void SimulateMissingProject()
    {
        _notifications.ShowError("Unable to load solution", "A project referenced by the solution could not be found.");
    }

    [RelayCommand]
    private void SimulateBrokenMsBuildProject()
    {
        _notifications.ShowError("Unable to load solution", "A project failed during design-time MSBuild evaluation.");
    }

    [RelayCommand]
    private void SimulateUnsupportedFileOpen()
    {
        OpenFileFailureNotification.TryShow(
            _notifications,
            "sample.png",
            new UnsupportedTextFileException("sample.png"));
    }

    [RelayCommand]
    private void SimulateDeletedFileOpen()
    {
        OpenFileFailureNotification.TryShow(
            _notifications,
            "DeletedFile.cs",
            new IOException("The file no longer exists."));
    }

    [RelayCommand]
    private void SimulateUnauthorizedFileOpen()
    {
        OpenFileFailureNotification.TryShow(
            _notifications,
            "Secrets.cs",
            new UnauthorizedAccessException("Access was denied."));
    }

    [RelayCommand]
    private async Task SaveActiveDocumentAsync(CancellationToken cancellationToken)
    {
        await _saveActiveDocumentHandler.HandleAsync(new SaveActiveDocumentCommand(), cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(HasWorkspace))]
    private async Task BuildAsync(CancellationToken cancellationToken)
    {
        IsTerminalExpanded = true;
        await _buildHandler.HandleAsync(new BuildWorkspaceCommand(), cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(HasWorkspace))]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        IsTerminalExpanded = true;
        await _runHandler.HandleAsync(new RunProjectCommand(), cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(HasWorkspace))]
    private async Task TestAsync(CancellationToken cancellationToken)
    {
        IsTerminalExpanded = true;
        await _testHandler.HandleAsync(new TestWorkspaceCommand(), cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(HasWorkspace))]
    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        IsTerminalExpanded = true;
        await _restoreHandler.HandleAsync(new RestoreWorkspaceCommand(), cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(HasWorkspace))]
    private async Task CleanAsync(CancellationToken cancellationToken)
    {
        IsTerminalExpanded = true;
        await _cleanHandler.HandleAsync(new CleanWorkspaceCommand(), cancellationToken);
    }

    private bool HasWorkspace() => WorkspaceMode != WorkspaceMode.Empty;

    [RelayCommand]
    private void CloseActiveDocument()
    {
        var activeDocument = _workspace.Current.TabSession.ActiveDocument;
        if (activeDocument is null)
        {
            return;
        }

        CloseDocument(activeDocument.Path);
    }

    private void ActivateDocument(string path)
    {
        _workspace.ActivateDocument(path);
    }

    private void CloseDocument(string path)
    {
        _workspace.CloseDocument(path);
    }
}
