using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.ViewModels;

namespace Fluence.Modules.Debug.ViewModels;

public sealed partial class DebugConsoleViewModel : ViewModelBase
{
    public const string ConsoleSubTabId = "Console";
    public const string VariablesSubTabId = "Variables";
    public const string WatchSubTabId = "Watch";

    [ObservableProperty]
    private string _activeSubTabId = ConsoleSubTabId;

    public DebugConsoleViewModel(
        DebugVariablesTabViewModel variables,
        DebugWatchTabViewModel watch)
    {
        Variables = variables;
        Watch = watch;

        SubTabs =
        [
            new DebugConsoleTabViewModel(ConsoleSubTabId, "Console", SelectSubTabCommand),
            new DebugConsoleTabViewModel(VariablesSubTabId, "Variables", SelectSubTabCommand),
            new DebugConsoleTabViewModel(WatchSubTabId, "Watch", SelectSubTabCommand),
        ];

        RefreshActiveTabs();
    }

    public IReadOnlyList<DebugConsoleTabViewModel> SubTabs { get; }

    public DebugVariablesTabViewModel Variables { get; }

    public DebugWatchTabViewModel Watch { get; }

    public bool IsConsoleActive => ActiveSubTabId == ConsoleSubTabId;

    public bool IsVariablesActive => ActiveSubTabId == VariablesSubTabId;

    public bool IsWatchActive => ActiveSubTabId == WatchSubTabId;

    [RelayCommand]
    private void SelectSubTab(string? subTabId)
    {
        if (string.IsNullOrWhiteSpace(subTabId))
            return;

        if (subTabId != ConsoleSubTabId && subTabId != VariablesSubTabId && subTabId != WatchSubTabId)
            return;

        ActiveSubTabId = subTabId;
    }

    partial void OnActiveSubTabIdChanged(string value)
    {
        RefreshActiveTabs();
        OnPropertyChanged(nameof(IsConsoleActive));
        OnPropertyChanged(nameof(IsVariablesActive));
        OnPropertyChanged(nameof(IsWatchActive));
    }

    private void RefreshActiveTabs()
    {
        foreach (var tab in SubTabs)
            tab.IsActive = tab.Id == ActiveSubTabId;
    }
}
