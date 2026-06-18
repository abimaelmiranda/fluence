using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Services.Modules;
using Fluence.Core.ViewModels;

namespace Fluence.Desktop.ViewModels;

public sealed partial class ActivityBarViewModel : ViewModelBase
{
    private readonly IShellEventBus _events;

    [ObservableProperty]
    private string? _activeTabId;

    public ActivityBarViewModel(IShellEventBus events)
    {
        _events = events;
        _events.SubscribeSync<ActivityBarTabSelectRequestedEvent>(e => SelectRequestedTab(e.TabId));
    }

    [RelayCommand]
    private void SelectTab(string tabId)
    {
        ActiveTabId = ActiveTabId == tabId ? null : tabId;
        _events.Publish(new ActivityBarTabChangedEvent(ActiveTabId));
    }

    public void RePublishActiveTab()
        => _events.Publish(new ActivityBarTabChangedEvent(ActiveTabId));

    private void SelectRequestedTab(string? tabId)
    {
        ActiveTabId = tabId;
        _events.Publish(new ActivityBarTabChangedEvent(ActiveTabId));
    }
}
