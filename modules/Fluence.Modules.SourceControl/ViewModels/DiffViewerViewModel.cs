using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Core.ViewModels;

namespace Fluence.Modules.SourceControl.ViewModels;

public sealed partial class DiffViewerViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _diffContent = string.Empty;

    public string Title { get; }

    public DiffViewerViewModel(string title, string diffContent)
    {
        Title = title;
        DiffContent = diffContent;
    }
}
