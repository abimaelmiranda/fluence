using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Exceptions;
using Fluence.Core.Modules;
using Fluence.Core.Ports;
using Fluence.Core.ViewModels;
using Fluence.Core.Workspace;

namespace Fluence.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    public const double DefaultTerminalHeight = 160;
    public const double MinimumTerminalHeight = 80;

    private readonly IWorkspaceContext _workspace;
    private readonly IUserNotificationService _notifications;
    private readonly IShellEventBus _eventBus;
    private readonly IShellRegionHost _regions;

    [ObservableProperty]
    private WorkspaceMode _workspaceMode;

    [ObservableProperty]
    private bool _isTerminalExpanded;

    private bool _isProvisioning;

    [ObservableProperty]
    private double _terminalHeight = DefaultTerminalHeight;

    public MainWindowViewModel(
        IWorkspaceContext workspace,
        WelcomeViewModel welcome,
        IUserNotificationService notifications,
        IShellEventBus eventBus,
        IShellRegionHost regions)
    {
        _workspace = workspace;
        _notifications = notifications;
        _eventBus = eventBus;
        _regions = regions;
        Welcome = welcome;
        _workspaceMode = workspace.Current.Mode;
        _workspace.Changed += OnWorkspaceChanged;
        _regions.Changed += OnShellRegionsChanged;
        _regions.RegionExpanded += OnShellRegionExpanded;
        _eventBus.Subscribe<ExpandPanelEvent>(OnExpandPanelRequested);
        _eventBus.Subscribe<DebuggerProvisioningRequiredEvent>(_ => SetProvisioning(true));
        _eventBus.Subscribe<DebuggerProvisioningFinishedEvent>(_ => SetProvisioning(false));
    }

    public WelcomeViewModel Welcome { get; }

    public object? ActiveSidebarContent => _regions.SidebarContent?.ViewModel;

    public object? MainEditorContent => _workspace.Current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.Tool
        ? _workspace.Current.TabSession.ActiveDocument.ContentViewModel
        : _regions.MainContent?.ViewModel;

    public object? TerminalContent => _regions.BottomBarContent?.ViewModel;

    public string SidebarTitle => _regions.SidebarContent?.Title ?? SidebarPlaceholder;

    public string BottomBarTitle => _regions.BottomBarContent?.Title ?? "Terminal";

    public bool IsWelcomeVisible => WorkspaceMode == WorkspaceMode.Empty;

    public bool IsWorkspaceVisible => WorkspaceMode != WorkspaceMode.Empty;

    public bool IsSidebarVisible => _regions.SidebarContent is not null;

    public bool IsFolderMode => WorkspaceMode == WorkspaceMode.Folder;

    public bool IsSolutionMode => WorkspaceMode == WorkspaceMode.Solution;

    public bool IsDebugging => WorkspaceMode == WorkspaceMode.Debugging;

    public bool HasActiveDocument => _workspace.Current.TabSession.ActiveDocument is not null;

    public bool HasNoActiveDocument => !HasActiveDocument;

    public IReadOnlyList<DocumentTabViewModel> OpenDocuments => _workspace.Current.TabSession.Documents
        .Select(document => new DocumentTabViewModel(
            document.Path,
            document.DisplayName,
            string.Equals(document.Path, ActiveDocumentPath, StringComparison.Ordinal),
            document.IsDirty,
            document.Kind == OpenDocumentKind.Tool,
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
        WorkspaceMode.Debugging => "Debugging",
        _ => "Fluence IDE",
    };

    public string? WorkspacePath => WorkspaceMode switch
    {
        WorkspaceMode.FileOnly => _workspace.Current.CurrentFilePath,
        WorkspaceMode.Folder => _workspace.Current.CurrentFolderPath,
        WorkspaceMode.Solution => _workspace.Current.CurrentSolutionPath,
        WorkspaceMode.Debugging => _workspace.Current.StartupProjectPath,
        _ => null,
    };

    public string EditorPlaceholder => WorkspaceMode switch
    {
        WorkspaceMode.FileOnly => "Open a text file to start editing",
        WorkspaceMode.Folder => "Open a file from the folder workspace",
        WorkspaceMode.Solution => "Open a file from the solution workspace",
        WorkspaceMode.Debugging => "Debug session active",
        _ => "Editor",
    };

    public string SidebarPlaceholder => WorkspaceMode switch
    {
        WorkspaceMode.Folder => "File Explorer",
        WorkspaceMode.Solution => "Solution View",
        WorkspaceMode.Debugging => "Debug",
        _ => "Sidebar",
    };

    public string SidebarDetail => WorkspaceMode switch
    {
        WorkspaceMode.Folder => _workspace.Current.CurrentFolderPath ?? "No folder opened",
        WorkspaceMode.Solution => _workspace.Current.CurrentSolutionPath ?? "No solution opened",
        WorkspaceMode.Debugging => _workspace.Current.StartupProjectPath ?? "Debug session",
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
        OnPropertyChanged(nameof(IsDebugging));
        OnPropertyChanged(nameof(ActiveSidebarContent));
        OnPropertyChanged(nameof(MainEditorContent));
        BuildCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();
        DebugCommand.NotifyCanExecuteChanged();
        StopDebugCommand.NotifyCanExecuteChanged();
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

    private void OnExpandPanelRequested(ExpandPanelEvent e)
    {
        if (e.PanelId == "Terminal")
            IsTerminalExpanded = true;
    }

    private void OnShellRegionExpanded(object? sender, ShellRegionExpandedEventArgs e)
    {
        if (e.Region == ShellRegion.BottomBar)
            IsTerminalExpanded = true;
    }

    private void OnShellRegionsChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ActiveSidebarContent));
        OnPropertyChanged(nameof(MainEditorContent));
        OnPropertyChanged(nameof(TerminalContent));
        OnPropertyChanged(nameof(SidebarTitle));
        OnPropertyChanged(nameof(BottomBarTitle));
        OnPropertyChanged(nameof(IsSidebarVisible));
    }

    [RelayCommand]
    private void ToggleTerminal()
    {
        IsTerminalExpanded = !IsTerminalExpanded;
    }

    public void SetTerminalHeight(double height, double maximumHeight)
    {
        var upperBound = Math.Max(MinimumTerminalHeight, maximumHeight);
        TerminalHeight = Math.Clamp(height, MinimumTerminalHeight, upperBound);
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
            new SimulatedUnsupportedFileException("This file does not appear to be a text file."));
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
        _eventBus.Publish(new SaveActiveDocumentRequestedEvent());
        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(HasWorkspace))]
    private async Task BuildAsync(CancellationToken cancellationToken)
    {
        IsTerminalExpanded = true;
        _eventBus.Publish(new BuildWorkspaceRequestedEvent());
        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanRunOrDebug))]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        IsTerminalExpanded = true;
        _eventBus.Publish(new RunProjectRequestedEvent());
        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanRunOrDebug))]
    private async Task DebugAsync(CancellationToken cancellationToken)
    {
        IsTerminalExpanded = true;
        _eventBus.Publish(new DebugProjectRequestedEvent());
        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(IsDebugging))]
    private async Task StopDebugAsync(CancellationToken cancellationToken)
    {
        _eventBus.Publish(new StopDebugRequestedEvent());
        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(HasWorkspace))]
    private async Task TestAsync(CancellationToken cancellationToken)
    {
        IsTerminalExpanded = true;
        _eventBus.Publish(new TestWorkspaceRequestedEvent());
        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(HasWorkspace))]
    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        IsTerminalExpanded = true;
        _eventBus.Publish(new RestoreWorkspaceRequestedEvent());
        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(HasWorkspace))]
    private async Task CleanAsync(CancellationToken cancellationToken)
    {
        IsTerminalExpanded = true;
        _eventBus.Publish(new CleanWorkspaceRequestedEvent());
        await Task.CompletedTask;
    }

    private bool HasWorkspace() => WorkspaceMode != WorkspaceMode.Empty;

    private bool CanRunOrDebug() => HasWorkspace() && !_isProvisioning;

    private void SetProvisioning(bool value)
    {
        _isProvisioning = value;
        Dispatcher.UIThread.Post(() =>
        {
            RunCommand.NotifyCanExecuteChanged();
            DebugCommand.NotifyCanExecuteChanged();
        });
    }

    [RelayCommand]
    private void CloseActiveDocument()
    {
        var activeDocument = _workspace.Current.TabSession.ActiveDocument;
        if (activeDocument is null)
            return;

        CloseDocument(activeDocument.Path);
    }

    private void ActivateDocument(string path) => _workspace.ActivateDocument(path);

    private void CloseDocument(string path) => _workspace.CloseDocument(path);

    private sealed class SimulatedUnsupportedFileException(string reason) : FluenceExceptionBase(reason)
    {
        public override string UserMessage { get; } = reason;
    }
}
