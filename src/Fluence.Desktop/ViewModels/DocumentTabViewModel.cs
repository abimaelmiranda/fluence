using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Fluence.Desktop.ViewModels;

public sealed class DocumentTabViewModel
{
    public DocumentTabViewModel(string path, string displayName, bool isActive, bool isDirty, Action<string> activate, Action<string> close)
    {
        Path = path;
        DisplayName = displayName;
        IsActive = isActive;
        IsDirty = isDirty;
        ActivateCommand = new RelayCommand(() => activate(Path));
        CloseCommand = new RelayCommand(() => close(Path));
    }

    public string Path { get; }

    public string DisplayName { get; }

    public bool IsActive { get; }

    public bool IsDirty { get; }

    public string DisplayTitle => IsDirty ? $"{DisplayName} *" : DisplayName;

    public ICommand ActivateCommand { get; }

    public ICommand CloseCommand { get; }
}
