using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.ViewModels;

public sealed partial class SourceControlViewModel
{
    private void ApplyStashes(System.Collections.Generic.IReadOnlyList<GitStash> stashes)
    {
        Stashes.Clear();
        foreach (var stash in stashes)
            Stashes.Add(new StashItemViewModel(stash, PopStash, DropStash));

        OnPropertyChanged(nameof(HasStashes));
    }

    private async void PopStash(GitStash stash)
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;

        IsBusy = true;
        try
        {
            await _git.PopStashAsync(stash.Ref, repoRoot);
            await RefreshCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async void DropStash(GitStash stash)
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;

        IsBusy = true;
        try
        {
            await _git.DropStashAsync(stash.Ref, repoRoot);
            await RefreshCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
