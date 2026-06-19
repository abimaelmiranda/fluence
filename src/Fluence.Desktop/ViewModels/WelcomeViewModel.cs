using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.File;
using Fluence.Core.ViewModels;
using Fluence.Core.Events.Workspace;

namespace Fluence.Desktop.ViewModels;

public sealed partial class WelcomeViewModel : ViewModelBase
{
    private readonly IWorkspaceDialogService _dialogs;
    private readonly IUserNotificationService _notifications;
    private readonly IShellEventBus _eventBus;
    private readonly IRecentProjectsService _recentProjectsService;
    private readonly ILocalizationService _loc;

    public WelcomeViewModel(
        IWorkspaceDialogService dialogs,
        IUserNotificationService notifications,
        IShellEventBus eventBus,
        IRecentProjectsService recentProjectsService,
        IWorkspaceContext workspace,
        ILocalizationService loc)
    {
        _dialogs = dialogs;
        _notifications = notifications;
        _eventBus = eventBus;
        _recentProjectsService = recentProjectsService;
        _loc = loc;

        ReloadRecents();
        workspace.Changed += OnWorkspaceChanged;
        _loc.LanguageChanged += OnLanguageChanged;
    }

    private string? _statusKey = "Desktop.Status.NoWorkspaceOpened";
    private string _statusRaw = string.Empty;

    public string Status => _statusKey != null ? _loc.Get(_statusKey) : _statusRaw;

    private void SetLocalizedStatus(string key)
    {
        _statusKey = key;
        OnPropertyChanged(nameof(Status));
    }

    private void SetRawStatus(string raw)
    {
        _statusKey = null;
        _statusRaw = raw;
        OnPropertyChanged(nameof(Status));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecentSolutions))]
    private ObservableCollection<RecentProject> _recentSolutions = [];

    public bool HasRecentSolutions => RecentSolutions.Count > 0;

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (sender is IWorkspaceContext ctx && ctx.Current.Mode == WorkspaceMode.Empty)
            ReloadRecents();
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Status));
    }

    private void ReloadRecents()
    {
        var recents = _recentProjectsService.GetRecents()
            .Where(recent => recent.Kind == RecentProjectKind.Solution)
            .ToArray();

        if (Dispatcher.UIThread.CheckAccess())
        {
            RecentSolutions.Clear();
            foreach (var r in recents)
                RecentSolutions.Add(r);
            OnPropertyChanged(nameof(HasRecentSolutions));
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                RecentSolutions.Clear();
                foreach (var r in recents)
                    RecentSolutions.Add(r);
                OnPropertyChanged(nameof(HasRecentSolutions));
            });
        }
    }

    [RelayCommand]
    private void NewProject()
    {
        _eventBus.Publish(new NewProjectRequestedEvent());
        SetLocalizedStatus("Desktop.Status.CreateProject");
    }

    [RelayCommand]
    private async Task OpenFileAsync(CancellationToken cancellationToken)
    {
        var path = await _dialogs.PickFileAsync(cancellationToken);
        if (path is null)
        {
            SetLocalizedStatus("Desktop.Status.NoWorkspaceOpened");
            return;
        }

        try
        {
            _eventBus.Publish(new OpenFileRequestedEvent(path));
            SetRawStatus(path);
        }
        catch (Exception ex) when (OpenFileFailureNotification.TryShow(_notifications, path, ex))
        {
            SetLocalizedStatus("Desktop.Status.UnableToOpenFile");
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync(CancellationToken cancellationToken)
    {
        var path = await _dialogs.PickFolderAsync(cancellationToken);
        if (path is null)
        {
            SetLocalizedStatus("Desktop.Status.NoWorkspaceOpened");
            return;
        }

        _eventBus.Publish(new OpenFolderRequestedEvent(path));
        SetRawStatus(path);
    }

    [RelayCommand]
    private async Task OpenSolutionAsync(CancellationToken cancellationToken)
    {
        var path = await _dialogs.PickSolutionAsync(cancellationToken);
        if (path is null)
        {
            SetLocalizedStatus("Desktop.Status.NoWorkspaceOpened");
            return;
        }

        _eventBus.Publish(new OpenSolutionRequestedEvent(path));
        SetRawStatus(path);
    }

    [RelayCommand]
    private void OpenRecent(RecentProject recent)
    {
        _eventBus.Publish(new OpenSolutionRequestedEvent(recent.Path));
        SetRawStatus(recent.Path);
    }
}
