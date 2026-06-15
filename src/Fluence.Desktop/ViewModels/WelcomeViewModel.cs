using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.File;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Services.File;
using Fluence.Core.ViewModels;

namespace Fluence.Desktop.ViewModels;

public sealed partial class WelcomeViewModel(
    IWorkspaceDialogService dialogs,
    IUserNotificationService notifications,
    IShellEventBus eventBus) : ViewModelBase
{
    [ObservableProperty]
    private string _status = "No workspace opened";

    [RelayCommand]
    private async Task OpenFileAsync(CancellationToken cancellationToken)
    {
        var path = await dialogs.PickFileAsync(cancellationToken);
        if (path is null)
        {
            Status = "No workspace opened";
            return;
        }

        try
        {
            eventBus.Publish(new OpenFileRequestedEvent(path));
            Status = path;
        }
        catch (Exception ex) when (OpenFileFailureNotification.TryShow(notifications, path, ex))
        {
            Status = "Unable to open file";
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync(CancellationToken cancellationToken)
    {
        var path = await dialogs.PickFolderAsync(cancellationToken);
        if (path is null)
        {
            Status = "No workspace opened";
            return;
        }

        eventBus.Publish(new OpenFolderRequestedEvent(path));
        Status = path;
    }

    [RelayCommand]
    private async Task OpenSolutionAsync(CancellationToken cancellationToken)
    {
        var path = await dialogs.PickSolutionAsync(cancellationToken);
        if (path is null)
        {
            Status = "No workspace opened";
            return;
        }

        eventBus.Publish(new OpenSolutionRequestedEvent(path));
        Status = path;
    }
}
