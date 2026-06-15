using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.File;
using Fluence.Core.ViewModels;

namespace Fluence.Desktop.ViewModels;

public sealed partial class WelcomeViewModel : ViewModelBase
{
    private readonly IWorkspaceDialogService _dialogs;
    private readonly IUserNotificationService _notifications;
    private readonly IShellEventBus _eventBus;
    private readonly IRecentProjectsService _recentProjectsService;

    public WelcomeViewModel(
        IWorkspaceDialogService dialogs,
        IUserNotificationService notifications,
        IShellEventBus eventBus,
        IRecentProjectsService recentProjectsService,
        IWorkspaceContext workspace)
    {
        _dialogs = dialogs;
        _notifications = notifications;
        _eventBus = eventBus;
        _recentProjectsService = recentProjectsService;

        ReloadRecents();
        workspace.Changed += OnWorkspaceChanged;
    }

    [ObservableProperty]
    private string _status = "No workspace opened";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecentProjects))]
    private ObservableCollection<RecentProject> _recentProjects = [];

    public bool HasRecentProjects => RecentProjects.Count > 0;

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (sender is IWorkspaceContext ctx && ctx.Current.Mode == WorkspaceMode.Empty)
            ReloadRecents();
    }

    private void ReloadRecents()
    {
        var recents = _recentProjectsService.GetRecents();
        if (Dispatcher.UIThread.CheckAccess())
        {
            RecentProjects.Clear();
            foreach (var r in recents)
                RecentProjects.Add(r);
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                RecentProjects.Clear();
                foreach (var r in recents)
                    RecentProjects.Add(r);
            });
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync(CancellationToken cancellationToken)
    {
        var path = await _dialogs.PickFileAsync(cancellationToken);
        if (path is null)
        {
            Status = "No workspace opened";
            return;
        }

        try
        {
            _eventBus.Publish(new OpenFileRequestedEvent(path));
            Status = path;
        }
        catch (Exception ex) when (OpenFileFailureNotification.TryShow(_notifications, path, ex))
        {
            Status = "Unable to open file";
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync(CancellationToken cancellationToken)
    {
        var path = await _dialogs.PickFolderAsync(cancellationToken);
        if (path is null)
        {
            Status = "No workspace opened";
            return;
        }

        _eventBus.Publish(new OpenFolderRequestedEvent(path));
        Status = path;
    }

    [RelayCommand]
    private async Task OpenSolutionAsync(CancellationToken cancellationToken)
    {
        var path = await _dialogs.PickSolutionAsync(cancellationToken);
        if (path is null)
        {
            Status = "No workspace opened";
            return;
        }

        _eventBus.Publish(new OpenSolutionRequestedEvent(path));
        Status = path;
    }

    [RelayCommand]
    private void OpenRecent(RecentProject recent)
    {
        if (recent.Kind == RecentProjectKind.Solution)
            _eventBus.Publish(new OpenSolutionRequestedEvent(recent.Path));
        else
            _eventBus.Publish(new OpenFolderRequestedEvent(recent.Path));

        Status = recent.Path;
    }
}
