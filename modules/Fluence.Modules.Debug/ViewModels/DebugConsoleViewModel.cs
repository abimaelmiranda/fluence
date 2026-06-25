using System.Collections.Generic;
using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.Output;
using Fluence.Core.ViewModels;

namespace Fluence.Modules.Debug.ViewModels;

public sealed partial class DebugConsoleViewModel : ViewModelBase, IDisposable
{
    public const string ConsoleSubTabId = "Console";
    public const string VariablesSubTabId = "Variables";
    public const string WatchSubTabId = "Watch";

    private readonly IOutputChannelService _channels;
    private readonly IUiDispatcher _dispatcher;

    [ObservableProperty]
    private string _activeSubTabId = ConsoleSubTabId;

    [ObservableProperty]
    private string _text = string.Empty;

    public DebugConsoleViewModel(
        DebugVariablesTabViewModel variables,
        DebugWatchTabViewModel watch,
        IOutputChannelService channels,
        IUiDispatcher dispatcher)
    {
        Variables = variables;
        Watch = watch;
        _channels = channels;
        _dispatcher = dispatcher;

        SubTabs =
        [
            new DebugConsoleTabViewModel(ConsoleSubTabId, "Console", SelectSubTabCommand),
            new DebugConsoleTabViewModel(VariablesSubTabId, "Variables", SelectSubTabCommand),
            new DebugConsoleTabViewModel(WatchSubTabId, "Watch", SelectSubTabCommand),
        ];

        RefreshActiveTabs();
        RefreshText();
        _channels.ChannelChanged += OnChannelChanged;
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

    [RelayCommand]
    private void Clear() => _channels.Clear(OutputChannelIds.Debug);

    private void OnChannelChanged(object? sender, OutputChannelChangedEventArgs e)
    {
        if (!string.Equals(e.ChannelId, OutputChannelIds.Debug, StringComparison.Ordinal))
            return;

        _dispatcher.Post(RefreshText);
    }

    private void RefreshText()
    {
        Text = string.Concat(_channels.GetEntries(OutputChannelIds.Debug).Select(e => e.Text));
    }

    public void Dispose()
    {
        _channels.ChannelChanged -= OnChannelChanged;
    }
}
