using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Modules;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.ViewModels;

public sealed partial class SourceControlViewModel
{
    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task CommitAsync()
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;

        IsBusy = true;
        try
        {
            await _git.CommitAsync(CommitMessage, repoRoot);
            CommitMessage = string.Empty;
            await RefreshCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCommit() => !string.IsNullOrWhiteSpace(CommitMessage) && StagedChanges.Count > 0;

    [RelayCommand]
    private async Task StageAllAsync()
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;

        foreach (var vm in UnstagedChanges.ToArray())
            await _git.StageAsync(vm.FilePath, repoRoot);

        await RefreshCoreAsync();
    }

    [RelayCommand]
    private async Task UnstageAllAsync()
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;

        foreach (var vm in StagedChanges.ToArray())
            await _git.UnstageAsync(vm.FilePath, repoRoot);

        await RefreshCoreAsync();
    }

    private async Task StageFileAsync(GitFileChange change)
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;

        await RunFileActionAsync(() => _git.StageAsync(change.FilePath, repoRoot));
    }

    private async Task UnstageFileAsync(GitFileChange change)
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;

        await RunFileActionAsync(() => _git.UnstageAsync(change.FilePath, repoRoot));
    }

    private async Task OpenDiffAsync(GitFileChange change)
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;

        try
        {
            var diff = await _git.GetDiffAsync(change.FilePath, change.IsStaged, repoRoot);
            var vm = new DiffViewerViewModel($"Diff: {change.FileName}", diff);
            _workspace.OpenToolTab($"diff:{change.FilePath}:{change.IsStaged}", vm.Title, vm);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Open diff failed for {change.FilePath}: {ex}");
        }
    }

    private Task OpenFileAsync(GitFileChange change)
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return Task.CompletedTask;

        var path = Path.IsPathRooted(change.FilePath)
            ? change.FilePath
            : Path.GetFullPath(Path.Combine(repoRoot, change.FilePath));

        try
        {
            _events.Publish(new OpenFileRequestedEvent(path));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Open changed file failed for {path}: {ex}");
        }

        return Task.CompletedTask;
    }

    private async Task RevertFileAsync(GitFileChange change)
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;

        var confirmed = await _dialogs.ConfirmRevertFileAsync(change);
        if (!confirmed) return;

        await RunFileActionAsync(() => _git.RevertFileAsync(change, repoRoot));

        var absolutePath = Path.IsPathRooted(change.FilePath)
            ? change.FilePath
            : Path.GetFullPath(Path.Combine(repoRoot, change.FilePath));

        if (File.Exists(absolutePath) &&
            _workspace.Current.TabSession.Documents.Any(d =>
                string.Equals(d.Path, absolutePath, StringComparison.OrdinalIgnoreCase)))
        {
            var content = await File.ReadAllTextAsync(absolutePath);
            _workspace.ReloadDocument(absolutePath, content);
        }
    }

    private async Task RunFileActionAsync(Func<Task> action)
    {
        try
        {
            await action();
            await RefreshCoreAsync();
        }
        catch (OperationCanceledException ex)
        {
            Debug.WriteLine($"Source control file action canceled: {ex}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Source control file action failed: {ex}");
        }
    }
}
