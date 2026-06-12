using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Fluence.Application.Workspace;
using Fluence.Core.Commands;
using Fluence.Core.Workspace;
using Fluence.Desktop.Services;

namespace Fluence.Desktop.ViewModels;

public sealed class FileExplorerViewModel : ViewModelBase
{
    private readonly IWorkspaceContext _workspace;
    private readonly ICommandHandler<OpenFileWorkspaceCommand> _openFileHandler;
    private readonly IUserNotificationService _notifications;
    private string? _currentFolderPath;

    public FileExplorerViewModel(
        IWorkspaceContext workspace,
        ICommandHandler<OpenFileWorkspaceCommand> openFileHandler,
        IUserNotificationService notifications)
    {
        _workspace = workspace;
        _openFileHandler = openFileHandler;
        _notifications = notifications;
        _workspace.Changed += OnWorkspaceChanged;
        RefreshRoot();
    }

    public ObservableCollection<FileTreeItem> RootItems { get; } = [];

    public string? RootName => Path.GetFileName(_workspace.Current.CurrentFolderPath);

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        var newFolderPath = _workspace.Current.CurrentFolderPath;

        if (!string.Equals(_currentFolderPath, newFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            RefreshRoot();
        }
        else
        {
            UpdateActiveItem(_workspace.Current.TabSession.ActiveDocument?.Path);
        }
    }

    private void UpdateActiveItem(string? activePath)
    {
        UpdateActiveItemRecursive(RootItems, activePath);
    }

    private static void UpdateActiveItemRecursive(
        System.Collections.Generic.IEnumerable<FileTreeItem> items,
        string? activePath)
    {
        foreach (var item in items)
        {
            item.IsActive = !item.IsDirectory &&
                            string.Equals(item.Path, activePath, StringComparison.OrdinalIgnoreCase);
            UpdateActiveItemRecursive(item.Children, activePath);
        }
    }

    private void RefreshRoot()
    {
        RootItems.Clear();

        var folderPath = _workspace.Current.CurrentFolderPath;
        _currentFolderPath = folderPath;

        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            return;
        }

        try
        {
            foreach (var dir in Directory.GetDirectories(folderPath))
            {
                RootItems.Add(FileTreeItem.CreateDirectory(dir, OnFileActivated));
            }

            foreach (var file in Directory.GetFiles(folderPath))
            {
                RootItems.Add(FileTreeItem.CreateFile(file, OnFileActivated));
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    private async void OnFileActivated(FileTreeItem item)
    {
        if (item.IsDirectory)
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
}
