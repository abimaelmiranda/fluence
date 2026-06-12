using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Fluence.Core.Ports;

namespace Fluence.Desktop.Services;

public sealed class AvaloniaWorkspaceDialogService : IWorkspaceDialogService
{
    private static readonly FilePickerFileType SolutionFileType = new("C# Solutions")
    {
        Patterns = ["*.sln", "*.slnx"],
    };

    public async Task<string?> PickFileAsync(CancellationToken cancellationToken = default)
    {
        var window = GetMainWindow();
        if (window is null)
        {
            return null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            AllowMultiple = false,
        });

        cancellationToken.ThrowIfCancellationRequested();

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        var window = GetMainWindow();
        if (window is null)
        {
            return null;
        }

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Folder",
            AllowMultiple = false,
        });

        cancellationToken.ThrowIfCancellationRequested();

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    public async Task<string?> PickSolutionAsync(CancellationToken cancellationToken = default)
    {
        var window = GetMainWindow();
        if (window is null)
        {
            return null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Solution",
            AllowMultiple = false,
            FileTypeFilter = [SolutionFileType],
        });

        cancellationToken.ThrowIfCancellationRequested();

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private static Window? GetMainWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }
}
