using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Exceptions;
using Fluence.Core.Abstractions.Keybindings;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Models.Keybindings;
using Fluence.Core.Models.Settings;
using Fluence.Core.Services;
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

namespace Fluence.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    public const double DefaultTerminalHeight = 160;
    public const double MinimumTerminalHeight = 80;

    private readonly IWorkspaceContext _workspace;
    private readonly IUserNotificationService _notifications;
    private readonly IShellEventBus _eventBus;
    private readonly IShellRegionHost _regions;
    private readonly ISettingsService _settings;
    private readonly ICommandRegistry _commands;
    private readonly IKeybindingService _keybindings;
    private readonly ISettingsTool _settingsTool;
    private readonly HashSet<string> _semanticTokensPendingFiles = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private WorkspaceMode _workspaceMode;

    [ObservableProperty]
    private bool _isSavingWorkspace;

    [ObservableProperty]
    private bool _isSemanticTokensLoading;

    [ObservableProperty]
    private string _semanticTokensStatusText = string.Empty;

    [ObservableProperty]
    private bool _isTerminalExpanded;

    private bool _isProvisioning;
    private bool _isSidebarExpanded = true;

    [ObservableProperty]
    private double _terminalHeight = DefaultTerminalHeight;

    [ObservableProperty]
    private double _sidebarWidth = 300;

    public MainWindowViewModel(
        IWorkspaceContext workspace,
        WelcomeViewModel welcome,
        IUserNotificationService notifications,
        IShellEventBus eventBus,
        IShellRegionHost regions,
        ActivityBarViewModel activityBar,
        ISettingsService settings,
        ICommandRegistry commands,
        IKeybindingService keybindings,
        ISettingsTool settingsTool)
    {
        _workspace = workspace;
        _notifications = notifications;
        _eventBus = eventBus;
        _regions = regions;
        _settings = settings;
        _commands = commands;
        _keybindings = keybindings;
        _settingsTool = settingsTool;
        ActivityBar = activityBar;
        Welcome = welcome;
        _workspaceMode = workspace.Current.Mode;
        ApplyShellSettings(_settings.Get<ShellSettings>());
        RegisterCommands();
        _settings.Watch<ShellSettings>().Subscribe(new ActionObserver<ShellSettings>(ApplyShellSettings));
        _workspace.Changed += OnWorkspaceChanged;
        _regions.Changed += OnShellRegionsChanged;
        _regions.RegionExpanded += OnShellRegionExpanded;
        _eventBus.SubscribeSync<ExpandPanelEvent>(OnExpandPanelRequested);
        _eventBus.SubscribeSync<DebuggerProvisioningRequiredEvent>(_ => SetProvisioning(true));
        _eventBus.SubscribeSync<DebuggerProvisioningFinishedEvent>(_ => SetProvisioning(false));
        _eventBus.SubscribeSync<SemanticTokensRefreshStartedEvent>(OnSemanticTokensRefreshStarted);
        _eventBus.SubscribeSync<SemanticTokensRefreshFinishedEvent>(OnSemanticTokensRefreshFinished);
        _eventBus.SubscribeSync<SemanticTokensRefreshFailedEvent>(OnSemanticTokensRefreshFailed);
    }

    public ActivityBarViewModel ActivityBar { get; }

    public WelcomeViewModel Welcome { get; }

    public object? ActiveSidebarContent => _regions.SidebarContent?.ViewModel;

    public object? MainEditorContent => _workspace.Current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.Tool
        ? _workspace.Current.TabSession.ActiveDocument.ContentViewModel
        : _regions.MainContent?.ViewModel;

    public object? TerminalContent => _regions.BottomBarContent?.ViewModel;

    public string SidebarTitle => _regions.SidebarContent?.Title ?? SidebarPlaceholder;

    public string BottomBarTitle => _regions.BottomBarContent?.Title ?? "Terminal";

    public bool IsWelcomeVisible => WorkspaceMode == WorkspaceMode.Empty && !HasActiveToolDocument;

    public bool IsWorkspaceVisible => WorkspaceMode != WorkspaceMode.Empty || HasActiveToolDocument;

    public bool IsSidebarVisible => _regions.SidebarContent is not null && _isSidebarExpanded;

    public bool IsFolderMode => WorkspaceMode == WorkspaceMode.Folder;

    public bool IsSolutionMode => WorkspaceMode == WorkspaceMode.Solution;

    public bool IsDebugging => WorkspaceMode == WorkspaceMode.Debugging;

    public bool HasActiveDocument => _workspace.Current.TabSession.ActiveDocument is not null;

    public bool HasNoActiveDocument => !HasActiveDocument;

    private bool HasActiveToolDocument => _workspace.Current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.Tool;

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
        PublishProjectCommand.NotifyCanExecuteChanged();
        ManageNuGetPackagesCommand.NotifyCanExecuteChanged();
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
        RefreshSemanticTokensStatus();
        ActivityBar.RePublishActiveTab();
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
        if (_regions.SidebarContent is not null)
            _isSidebarExpanded = true;
        OnPropertyChanged(nameof(ActiveSidebarContent));
        OnPropertyChanged(nameof(MainEditorContent));
        OnPropertyChanged(nameof(TerminalContent));
        OnPropertyChanged(nameof(SidebarTitle));
        OnPropertyChanged(nameof(BottomBarTitle));
        OnPropertyChanged(nameof(IsSidebarVisible));
    }

    private void OnSemanticTokensRefreshStarted(SemanticTokensRefreshStartedEvent e)
    {
        _semanticTokensPendingFiles.Add(e.FilePath);
        RefreshSemanticTokensStatus();
    }

    private void OnSemanticTokensRefreshFinished(SemanticTokensRefreshFinishedEvent e)
    {
        _semanticTokensPendingFiles.Remove(e.FilePath);
        RefreshSemanticTokensStatus();
    }

    private void OnSemanticTokensRefreshFailed(SemanticTokensRefreshFailedEvent e)
    {
        _semanticTokensPendingFiles.Remove(e.FilePath);
        RefreshSemanticTokensStatus();
    }

    private void RefreshSemanticTokensStatus()
    {
        var activePath = ActiveDocumentPath;
        IsSemanticTokensLoading = activePath is not null &&
                                  _semanticTokensPendingFiles.Contains(activePath);
        SemanticTokensStatusText = IsSemanticTokensLoading ? "Analyzing C#..." : string.Empty;
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

    public void PersistShellLayout()
    {
        _settings.Update<ShellSettings>(settings =>
        {
            settings.TerminalHeight = TerminalHeight;
            settings.SidebarWidth = SidebarWidth;
        });
    }

    public Task<bool> TryHandleKeybindingAsync(string scope, string key) =>
        _keybindings.TryExecuteAsync(scope, key);

    private void ApplyShellSettings(ShellSettings settings)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SidebarWidth = Math.Max(220, settings.SidebarWidth);
            TerminalHeight = Math.Max(MinimumTerminalHeight, settings.TerminalHeight);
        });
    }

    private void RegisterCommands()
    {
        var primary = OperatingSystem.IsMacOS() ? "Meta" : "Ctrl";
        _commands.Register(new IdeCommandDefinition(
            CommandIds.SaveActiveDocument,
            "Save",
            KeybindingScope.Global,
            $"{primary}+S",
            SaveActiveDocumentAsync));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.ToggleTerminal,
            "Toggle Terminal",
            KeybindingScope.Global,
            $"{primary}+J",
            ct =>
            {
                ToggleTerminal();
                return Task.CompletedTask;
            }));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.Build,
            "Build",
            KeybindingScope.Global,
            $"{primary}+Shift+B",
            BuildAsync));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.Restore,
            "Restore",
            KeybindingScope.Global,
            null,
            RestoreAsync));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.Run,
            "Run",
            KeybindingScope.Global,
            "F5",
            RunAsync));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.Debug,
            "Debug",
            KeybindingScope.Global,
            "Shift+F5",
            DebugAsync));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.StopDebug,
            "Stop Debugging",
            KeybindingScope.Global,
            "Ctrl+F5",
            StopDebugAsync));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.Test,
            "Test",
            KeybindingScope.Global,
            null,
            TestAsync));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.Clean,
            "Clean",
            KeybindingScope.Global,
            null,
            CleanAsync));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.Publish,
            "Publish",
            KeybindingScope.Global,
            null,
            ct =>
            {
                PublishProject();
                return Task.CompletedTask;
            }));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.OpenDotnetSdkSetup,
            "Open .NET SDK",
            KeybindingScope.Global,
            null,
            ct =>
            {
                OpenDotnetSdkSetup();
                return Task.CompletedTask;
            }));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.ManageNuGetPackages,
            "Manage NuGet Packages",
            KeybindingScope.Global,
            null,
            ct =>
            {
                ManageNuGetPackages();
                return Task.CompletedTask;
            }));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.OpenSettings,
            "Open Settings",
            KeybindingScope.Global,
            $"{primary}+Shift+Comma",
            ct =>
            {
                OpenSettings();
                return Task.CompletedTask;
            }));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.OpenKeybindings,
            "Open Keyboard Shortcuts",
            KeybindingScope.Global,
            $"{primary}+K",
            ct =>
            {
                OpenKeybindings();
                return Task.CompletedTask;
            }));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.NextTab,
            "Next Tab",
            KeybindingScope.Global,
            "Ctrl+Tab",
            NextTabAsync));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.ToggleSidebar,
            "Toggle Sidebar",
            KeybindingScope.Global,
            $"{primary}+B",
            ct =>
            {
                _isSidebarExpanded = !_isSidebarExpanded;
                OnPropertyChanged(nameof(IsSidebarVisible));
                return Task.CompletedTask;
            }));
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
    private void OpenSettings()
    {
        _settingsTool.ShowSettings();
        _workspace.OpenToolTab(ToolTabIds.Settings, ToolTabIds.SettingsTitle, _settingsTool);
    }

    [RelayCommand]
    private void OpenKeybindings()
    {
        _settingsTool.ShowKeybindings();
        _workspace.OpenToolTab(ToolTabIds.Settings, ToolTabIds.SettingsTitle, _settingsTool);
    }

    [RelayCommand]
    private void NewProject()
    {
        _eventBus.Publish(new NewProjectRequestedEvent());
    }

    [RelayCommand]
    private void OpenDotnetSdkSetup()
    {
        _eventBus.Publish(new DotnetSdkSetupRequestedEvent());
    }

    [RelayCommand(CanExecute = nameof(CanManageNuGetPackages))]
    private void ManageNuGetPackages()
    {
        if (!CanManageNuGetPackages())
            return;

        _eventBus.Publish(new ManageNuGetPackagesRequestedEvent(_workspace.Current.CurrentSolutionPath!));
    }

    [RelayCommand(CanExecute = nameof(CanPublishProject))]
    private void PublishProject()
    {
        if (!CanPublishProject())
            return;

        _eventBus.Publish(new PublishProjectRequestedEvent());
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
        if (!HasWorkspace())
            return;

        IsTerminalExpanded = true;
        _eventBus.Publish(new BuildWorkspaceRequestedEvent());
        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanRunOrDebug))]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!CanRunOrDebug())
            return;

        IsTerminalExpanded = true;
        _eventBus.Publish(new RunProjectRequestedEvent());
        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanRunOrDebug))]
    private async Task DebugAsync(CancellationToken cancellationToken)
    {
        if (!CanRunOrDebug())
            return;

        IsTerminalExpanded = true;
        _eventBus.Publish(new DebugProjectRequestedEvent());
        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(IsDebugging))]
    private async Task StopDebugAsync(CancellationToken cancellationToken)
    {
        if (!IsDebugging)
            return;

        _eventBus.Publish(new StopDebugRequestedEvent());
        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(HasWorkspace))]
    private async Task TestAsync(CancellationToken cancellationToken)
    {
        if (!HasWorkspace())
            return;

        IsTerminalExpanded = true;
        _eventBus.Publish(new TestWorkspaceRequestedEvent());
        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(HasWorkspace))]
    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (!HasWorkspace())
            return;

        IsTerminalExpanded = true;
        _eventBus.Publish(new RestoreWorkspaceRequestedEvent());
        await Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(HasWorkspace))]
    private async Task CleanAsync(CancellationToken cancellationToken)
    {
        if (!HasWorkspace())
            return;

        IsTerminalExpanded = true;
        _eventBus.Publish(new CleanWorkspaceRequestedEvent());
        await Task.CompletedTask;
    }

    private bool HasWorkspace() => WorkspaceMode != WorkspaceMode.Empty;

    private bool CanRunOrDebug() => HasWorkspace() && !_isProvisioning;

    private bool CanPublishProject() => WorkspaceMode == WorkspaceMode.Solution &&
                                        !string.IsNullOrWhiteSpace(_workspace.Current.CurrentSolutionPath);

    private bool CanManageNuGetPackages() => CanPublishProject();

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

    private Task NextTabAsync(CancellationToken cancellationToken)
    {
        var tabSession = _workspace.Current.TabSession;
        var documents = tabSession.Documents;
        if (documents.Count <= 1)
            return Task.CompletedTask;

        var activeIndex = -1;
        for (var i = 0; i < documents.Count; i++)
        {
            if (string.Equals(documents[i].Path, tabSession.ActiveDocument?.Path, StringComparison.Ordinal))
            {
                activeIndex = i;
                break;
            }
        }

        var nextIndex = (activeIndex + 1) % documents.Count;
        _workspace.ActivateDocument(documents[nextIndex].Path);
        return Task.CompletedTask;
    }

    private void CloseDocument(string path) => _workspace.CloseDocument(path);

    private sealed class SimulatedUnsupportedFileException(string reason) : FluenceExceptionBase(reason)
    {
        public override string UserMessage { get; } = reason;
    }
}
