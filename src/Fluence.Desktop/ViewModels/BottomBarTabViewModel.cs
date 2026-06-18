using CommunityToolkit.Mvvm.Input;
using Fluence.Core.ViewModels;

namespace Fluence.Desktop.ViewModels;

public sealed partial class BottomBarTabViewModel(
    string id,
    string title,
    IRelayCommand<string?> selectCommand)
    : ViewModelBase
{
    private bool _isActive;

    public string Id { get; } = id;

    public string Title { get; } = title;

    public IRelayCommand<string?> SelectCommand { get; } = selectCommand;

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}
