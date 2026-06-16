using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace Fluence.Modules.SourceControl.ViewModels;

public sealed partial class SourceControlViewModel
{
    [RelayCommand]
    private async Task PullAsync()
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;

        IsBusy = true;
        try
        {
            await _git.PullAsync(repoRoot);
            await RefreshCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PushAsync()
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;

        IsBusy = true;
        try
        {
            await _git.PushAsync(repoRoot);
            await RefreshCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task FetchAsync()
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;

        IsBusy = true;
        try
        {
            await _git.FetchAsync(repoRoot);
            await RefreshCoreAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
