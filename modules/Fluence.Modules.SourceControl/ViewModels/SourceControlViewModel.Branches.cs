using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Modules;
using Fluence.Modules.SourceControl.Models;
using Fluence.Core.Events.Git;

namespace Fluence.Modules.SourceControl.ViewModels;

public sealed partial class SourceControlViewModel
{
    private void CheckoutBranch(GitBranch branch) => FireAndForget(() => CheckoutBranchCoreAsync(branch));

    private async Task CheckoutBranchCoreAsync(GitBranch branch)
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;

        IsBusy = true;
        try
        {
            var result = await _git.CheckoutBranchAsync(branch.Name, repoRoot);
            if (result == CheckoutResult.Success)
            {
                _events.Publish(new GitCheckoutCompletedEvent(branch.Name));
                await RefreshCoreAsync();
            }
            else if (result == CheckoutResult.HasLocalChanges)
            {
                _pendingCheckoutBranchData = branch;
                PendingCheckoutBranch = branch.Name;
                HasPendingCheckoutConflict = true;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StashAndCheckoutAsync()
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null || _pendingCheckoutBranchData is null) return;

        IsBusy = true;
        var branch = _pendingCheckoutBranchData;
        ClearCheckoutConflict();
        try
        {
            await _git.StashAsync(repoRoot);
            await _git.CheckoutBranchAsync(branch.Name, repoRoot);
            _events.Publish(new GitCheckoutCompletedEvent(branch.Name));
            await RefreshCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ForceCheckoutAsync()
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null || _pendingCheckoutBranchData is null) return;

        IsBusy = true;
        var branch = _pendingCheckoutBranchData;
        ClearCheckoutConflict();
        try
        {
            await _git.ForceCheckoutBranchAsync(branch.Name, repoRoot);
            _events.Publish(new GitCheckoutCompletedEvent(branch.Name));
            await RefreshCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelCheckout() => ClearCheckoutConflict();

    private void ClearCheckoutConflict()
    {
        HasPendingCheckoutConflict = false;
        PendingCheckoutBranch = string.Empty;
        _pendingCheckoutBranchData = null;
    }

    private void DeleteBranch(GitBranch branch) => FireAndForget(() => DeleteBranchCoreAsync(branch));

    private async Task DeleteBranchCoreAsync(GitBranch branch)
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;

        IsBusy = true;
        try
        {
            await _git.DeleteBranchAsync(branch.Name, force: false, repoRoot);
            await RefreshCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyBranches(System.Collections.Generic.IReadOnlyList<GitBranch> branches)
    {
        Branches.ReplaceAll(branches.Select(b => new BranchItemViewModel(b, CheckoutBranch, DeleteBranch)));
    }
}
