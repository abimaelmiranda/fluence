using System;
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

    public GitFileChangeViewModel(
        GitFileChange change,
        Action<GitFileChange> stage,
        Action<GitFileChange> unstage,
        Action<GitFileChange> openDiff)
    {
        _change         = change;
        StageCommand    = new RelayCommand(() => stage(change));
        UnstageCommand  = new RelayCommand(() => unstage(change));
        OpenDiffCommand = new RelayCommand(() => openDiff(change));
    }
}
