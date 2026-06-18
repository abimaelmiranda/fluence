using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.ViewModels;

public sealed class StashItemViewModel
{
    private readonly GitStash _stash;

    public string Ref          => _stash.Ref;
    public string Message      => _stash.Message;
    public string RelativeDate => _stash.RelativeDate;

    public ICommand PopCommand  { get; }
    public ICommand DropCommand { get; }

    public StashItemViewModel(GitStash stash, Action<GitStash> pop, Action<GitStash> drop)
    {
        _stash      = stash;
        PopCommand  = new RelayCommand(() => pop(stash));
        DropCommand = new RelayCommand(() => drop(stash));
    }
}
