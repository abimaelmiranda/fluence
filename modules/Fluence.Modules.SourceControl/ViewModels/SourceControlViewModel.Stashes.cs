using System.Threading.Tasks;
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

    private void PopStash(GitStash stash) => FireAndForget(() => PopStashCoreAsync(stash));

    private async Task PopStashCoreAsync(GitStash stash)
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

    private void DropStash(GitStash stash) => FireAndForget(() => DropStashCoreAsync(stash));

    private async Task DropStashCoreAsync(GitStash stash)
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
