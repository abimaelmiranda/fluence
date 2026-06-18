using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.ViewModels;

public sealed class BranchItemViewModel
{
    private readonly GitBranch _branch;

    public string Name      => _branch.Name;
    public bool IsCurrent   => _branch.IsCurrent;
    public bool IsLocal     => _branch.IsLocal;
    public bool IsRemote    => _branch.IsRemote;
    public bool CanDelete   => !_branch.IsCurrent && _branch.IsLocal;
    public bool CanCheckout => !_branch.IsCurrent;

    public ICommand CheckoutCommand { get; }
    public ICommand DeleteCommand   { get; }

    public BranchItemViewModel(
        GitBranch branch,
        Action<GitBranch> checkout,
        Action<GitBranch> delete)
    {
        _branch         = branch;
        CheckoutCommand = new RelayCommand(() => checkout(branch));
        DeleteCommand   = new RelayCommand(() => delete(branch));
    }
}
