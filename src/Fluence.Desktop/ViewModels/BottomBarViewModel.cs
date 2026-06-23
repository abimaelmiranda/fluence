using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Workbench;
using Fluence.Core.ViewModels;

namespace Fluence.Desktop.ViewModels;

public sealed partial class BottomBarViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _activeTabId = BottomBarTabIds.Terminal;

    public BottomBarViewModel(IOutputChannelService channels, IOutputChannelRegistry registry, ProblemsViewModel problems)
    {
        Output = new OutputChannelViewModel(channels, registry);
        Debug = new OutputChannelViewModel(channels, registry, OutputChannelIds.Debug);
        Run = new OutputChannelViewModel(channels, registry, OutputChannelIds.Run);
        Problems = problems;

        Tabs =
        [
            new BottomBarTabViewModel(BottomBarTabIds.Output, "Output", SelectTabCommand),
            new BottomBarTabViewModel(BottomBarTabIds.Debug, "Debug", SelectTabCommand),
            new BottomBarTabViewModel(BottomBarTabIds.Terminal, "Terminal", SelectTabCommand),
            new BottomBarTabViewModel(BottomBarTabIds.Run, "Run", SelectTabCommand),
            new BottomBarTabViewModel(BottomBarTabIds.Problems, "Problems", SelectTabCommand),
        ];

        RefreshActiveTabs();
    }

    public ObservableCollection<BottomBarTabViewModel> Tabs { get; }

    public OutputChannelViewModel Output { get; }

    public OutputChannelViewModel Debug { get; }

    public OutputChannelViewModel Run { get; }

    public ProblemsViewModel Problems { get; }

    public bool IsOutputActive => ActiveTabId == BottomBarTabIds.Output;

    public bool IsDebugActive => ActiveTabId == BottomBarTabIds.Debug;

    public bool IsTerminalActive => ActiveTabId == BottomBarTabIds.Terminal;

    public bool IsRunActive => ActiveTabId == BottomBarTabIds.Run;

    public bool IsProblemsActive => ActiveTabId == BottomBarTabIds.Problems;

    [RelayCommand]
    public void SelectTab(string? tabId)
    {
        if (string.IsNullOrWhiteSpace(tabId) || Tabs.All(tab => tab.Id != tabId))
            return;

        ActiveTabId = tabId;
    }

    partial void OnActiveTabIdChanged(string value)
    {
        RefreshActiveTabs();
        OnPropertyChanged(nameof(IsOutputActive));
        OnPropertyChanged(nameof(IsDebugActive));
        OnPropertyChanged(nameof(IsTerminalActive));
        OnPropertyChanged(nameof(IsRunActive));
        OnPropertyChanged(nameof(IsProblemsActive));
    }

    private void RefreshActiveTabs()
    {
        foreach (var tab in Tabs)
            tab.IsActive = tab.Id == ActiveTabId;
    }
}
