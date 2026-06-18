using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.ViewModels;

public sealed class GitFileChangeViewModel
{
    private readonly GitFileChange _change;

    public string FileName      => _change.FileName;
    public string FilePath      => _change.FilePath;
    public string StatusLabel   => _change.StatusLabel;
    public string StatusColor   => _change.StatusColor;
    public bool   IsStaged      => _change.IsStaged;
    public GitFileChange Change => _change;

    public ICommand StageCommand    { get; }
    public ICommand UnstageCommand  { get; }
    public ICommand OpenDiffCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand RevertCommand   { get; }

    public GitFileChangeViewModel(
        GitFileChange change,
        Func<GitFileChange, Task> stage,
        Func<GitFileChange, Task> unstage,
        Func<GitFileChange, Task> openDiff,
        Func<GitFileChange, Task> openFile,
        Func<GitFileChange, Task> revert)
    {
        _change         = change;
        StageCommand    = new AsyncRelayCommand(() => stage(change));
        UnstageCommand  = new AsyncRelayCommand(() => unstage(change));
        OpenDiffCommand = new AsyncRelayCommand(() => openDiff(change));
        OpenFileCommand = new AsyncRelayCommand(() => openFile(change));
        RevertCommand   = new AsyncRelayCommand(() => revert(change));
    }
}
