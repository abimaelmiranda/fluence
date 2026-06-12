using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Application.Workspace;
using Fluence.Core.Commands;
using Fluence.Desktop.Services;

namespace Fluence.Desktop.ViewModels;

public sealed partial class WelcomeViewModel(
    IWorkspaceDialogService dialogs,
    IUserNotificationService notifications,
    ICommandHandler<OpenFileWorkspaceCommand> openFileHandler,
    ICommandHandler<OpenFolderWorkspaceCommand> openFolderHandler,
    ICommandHandler<OpenSolutionWorkspaceCommand> openSolutionHandler) : ViewModelBase
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
            await openFileHandler.HandleAsync(new OpenFileWorkspaceCommand(path), cancellationToken);
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

        await openFolderHandler.HandleAsync(new OpenFolderWorkspaceCommand(path), cancellationToken);
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

        await openSolutionHandler.HandleAsync(new OpenSolutionWorkspaceCommand(path), cancellationToken);
        Status = path;
    }
}
