using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Events.Workspace;
using Fluence.Desktop.Services;
using Fluence.Core.ViewModels;

namespace Fluence.Desktop.ViewModels;

public sealed partial class QuickOpenViewModel : ViewModelBase
{
    private readonly FileIndexService _index;
    private readonly IShellEventBus _eventBus;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private FileSearchResult? _selectedResult;

    public ObservableCollection<FileSearchResult> Results { get; } = [];

    public QuickOpenViewModel(FileIndexService index, IShellEventBus eventBus)
    {
        _index = index;
        _eventBus = eventBus;
    }

    public void Open()
    {
        SearchQuery = string.Empty;
        RefreshResults();
        IsVisible = true;
    }

    public void Close()
    {
        IsVisible = false;
    }

    public void Confirm()
    {
        var result = SelectedResult ?? (Results.Count > 0 ? Results[0] : null);
        if (result is null)
            return;

        Close();
        _eventBus.Publish(new OpenFileRequestedEvent(result.AbsolutePath));
    }

    partial void OnSearchQueryChanged(string value) => RefreshResults();

    private void RefreshResults()
    {
        Results.Clear();
        foreach (var item in _index.Search(SearchQuery))
            Results.Add(item);

        SelectedResult = Results.Count > 0 ? Results[0] : null;
    }
}
