using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
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
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;

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
        _eventBus.Subscribe<GitCheckoutCompletedEvent>(OnGitCheckoutCompleted);
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

    private void OnGitCheckoutCompleted(GitCheckoutCompletedEvent _)
        => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshRoot);

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
        item.NewFileCommand = null;
        item.NewFolderCommand = null;
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
        item.NewFileCommand = new AsyncRelayCommand(() => CreateFileAsync(item));
        item.NewFolderCommand = new AsyncRelayCommand(() => CreateFolderAsync(item));
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

    private async Task CreateFolderAsync(FileTreeItem item)
    {
        if (!item.IsDirectory)
        {
            return;
        }

        try
        {
            var folderName = await _fileDialogs.PromptForNameAsync("New Folder", "Folder name");
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return;
            }

            EnsureValidFileSystemName(folderName);

            var targetPath = Path.Combine(item.Path, folderName);
            if (Directory.Exists(targetPath) || File.Exists(targetPath))
            {
                _notifications.ShowWarning("Create folder", "A file or folder with that name already exists.");
                return;
            }

            _fileService.CreateDirectory(targetPath);
            item.ReloadChildren();
        }
        catch (Exception ex)
        {
            _notifications.ShowError("Unable to create folder", ex.Message);
        }
    }

    private async Task CreateFileAsync(FileTreeItem item)
    {
        if (!item.IsDirectory)
        {
            return;
        }

        try
        {
            var fileName = await _fileDialogs.PromptForNameAsync("New File", "File name");
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            EnsureValidFileSystemName(fileName);

            var targetPath = Path.Combine(item.Path, fileName);
            if (Directory.Exists(targetPath) || File.Exists(targetPath))
            {
                _notifications.ShowWarning("Create file", "A file or folder with that name already exists.");
                return;
            }

            _fileService.WriteText(targetPath, string.Empty);
            item.ReloadChildren();
            _eventBus.Publish(new OpenFileRequestedEvent(targetPath));
        }
        catch (Exception ex)
        {
            _notifications.ShowError("Unable to create file", ex.Message);
        }
    }

    private static void EnsureValidFileSystemName(string name)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException("The name contains invalid characters.");
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
