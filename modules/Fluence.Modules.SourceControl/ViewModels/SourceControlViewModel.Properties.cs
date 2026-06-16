using CommunityToolkit.Mvvm.ComponentModel;

namespace Fluence.Modules.SourceControl.ViewModels;

public sealed partial class SourceControlViewModel
{
    [ObservableProperty]
    private bool _isGitRepository;

    [ObservableProperty]
    private string _currentBranch = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    private string _commitMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAhead))]
    private int _aheadCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBehind))]
    private int _behindCount;

    [ObservableProperty]
    private bool _hasPendingCheckoutConflict;

    [ObservableProperty]
    private string _pendingCheckoutBranch = string.Empty;
}
