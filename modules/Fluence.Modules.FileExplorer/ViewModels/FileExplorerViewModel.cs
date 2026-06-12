using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Modules;
using Fluence.Core.Ports;
using Fluence.Core.ViewModels;
using Fluence.Core.Workspace;

namespace Fluence.Modules.FileExplorer.ViewModels;

public sealed class FileExplorerViewModel : ViewModelBase
{
    private readonly IWorkspaceContext _workspace;
    private readonly IShellEventBus _eventBus;
    private readonly IUserNotificationService _notifications;
    private readonly IFileClipboardService _clipboard;
    private readonly IFileOperationDialogService _fileDialogs;
    private readonly IFileService _fileService;
    private string? _currentFolderPath;

    public FileExplorerViewModel(
        IWorkspaceContext workspace,
        IShellEventBus eventBus,
        IUserNotificationService notifications,
        IFileClipboardService clipboard,
        IFileOperationDialogService fileDialogs,
        IFileService fileService)
    {
        _workspace = workspace;
        _eventBus = eventBus;
        _notifications = notifications;
        _clipboard = clipboard;
        _fileDialogs = fileDialogs;
        _fileService = fileService;
        _workspace.Changed += OnWorkspaceChanged;
        RefreshRoot();
    }

    public ObservableCollection<FileTreeItem> RootItems { get; } = [];

    public string? RootName => Path.GetFileName(_workspace.Current.CurrentFolderPath);

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        var newFolderPath = _workspace.Current.CurrentFolderPath;

        if (!string.Equals(_currentFolderPath, newFolderPath, StringComparison.OrdinalIgnoreCase))
            RefreshRoot();
        else
            UpdateActiveItem(_workspace.Current.TabSession.ActiveDocument?.Path);
    }

    private void UpdateActiveItem(string? activePath) => UpdateActiveItemRecursive(RootItems, activePath);

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
            return;

        try
        {
            foreach (var dir in Directory.GetDirectories(folderPath))
                RootItems.Add(CreateDirectoryItem(dir));

            foreach (var file in Directory.GetFiles(folderPath))
                RootItems.Add(CreateFileItem(file));
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        UpdateActiveItem(_workspace.Current.TabSession.ActiveDocument?.Path);
    }

    private FileTreeItem CreateFileItem(string path)
    {
        var item = FileTreeItem.CreateFile(path, CreateFileItem, CreateDirectoryItem);
        item.OpenCommand = new RelayCommand(() => OpenItem(item));
        item.CopyCommand = new RelayCommand(() => _clipboard.Copy(item.Path));
        item.DeleteCommand = new AsyncRelayCommand(() => DeleteAsync(item));
        item.LoadSolutionCommand = item.IsSolutionFile
            ? new RelayCommand(() => _eventBus.Publish(new OpenSolutionRequestedEvent(item.Path)))
            : null;
        return item;
    }

    private FileTreeItem CreateDirectoryItem(string path)
    {
        var item = FileTreeItem.CreateDirectory(path, CreateFileItem, CreateDirectoryItem);
        item.OpenCommand = new RelayCommand(() => item.IsExpanded = !item.IsExpanded);
        item.CopyCommand = new RelayCommand(() => _clipboard.Copy(item.Path));
        item.PasteCommand = new AsyncRelayCommand(() => PasteAsync(item));
        item.DeleteCommand = new AsyncRelayCommand(() => DeleteAsync(item));
        return item;
    }

    private void OpenItem(FileTreeItem item)
    {
        if (item.IsDirectory)
        {
            item.IsExpanded = !item.IsExpanded;
            return;
        }

        try
        {
            _eventBus.Publish(new OpenFileRequestedEvent(item.Path));
        }
        catch (Exception ex) when (OpenFileFailureNotification.TryShow(_notifications, item.Path, ex))
        {
        }
    }

    private async Task PasteAsync(FileTreeItem item)
    {
        if (!item.IsDirectory)
        {
            return;
        }

        try
        {
            await _clipboard.PasteAsync(item.Path);
            RefreshRoot();
        }
        catch (Exception ex)
        {
            _notifications.ShowError("Unable to paste", ex.Message);
        }
    }

    private async Task DeleteAsync(FileTreeItem item)
    {
        try
        {
            var confirmed = await _fileDialogs.ConfirmDeleteAsync(item.Path, item.IsDirectory);
            if (!confirmed)
                return;

            _fileService.Delete(item.Path, item.IsDirectory);
            RefreshRoot();
        }
        catch (Exception ex)
        {
            _notifications.ShowError("Unable to delete", ex.Message);
        }
    }
}
