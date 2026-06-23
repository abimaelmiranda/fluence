using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Services.Debugging;
using Fluence.Core.ViewModels;

namespace Fluence.Modules.Debug.ViewModels;

public sealed partial class DebugWatchTabViewModel : ViewModelBase, IDisposable
{
    private readonly IDebugService _debugService;
    private readonly IDebugStateService _debugState;
    private readonly ILocalizationService _loc;

    public DebugWatchTabViewModel(
        IDebugService debugService,
        IDebugStateService debugState,
        ILocalizationService loc)
    {
        _debugService = debugService;
        _debugState = debugState;
        _loc = loc;
        _debugState.Changed += OnDebugStateChanged;
    }

    [ObservableProperty]
    private string _newExpression = string.Empty;

    public ObservableCollection<DebugWatchEntry> Entries { get; } = [];

    [RelayCommand]
    private async Task AddExpressionAsync()
    {
        var expr = NewExpression.Trim();
        if (string.IsNullOrEmpty(expr))
            return;

        var entry = new DebugWatchEntry(expr, RemoveEntry, _debugService, _debugState, _loc);
        Entries.Add(entry);
        NewExpression = string.Empty;
        await entry.EvaluateAsync();
    }

    private void RemoveEntry(DebugWatchEntry entry) => Entries.Remove(entry);

    private void OnDebugStateChanged(object? sender, EventArgs e)
    {
        if (!_debugState.Snapshot.IsStopped)
            return;

        Dispatcher.UIThread.Post(async () =>
        {
            foreach (var entry in Entries.ToArray())
                await entry.EvaluateAsync();
        });
    }

    public void Dispose()
    {
        _debugState.Changed -= OnDebugStateChanged;
    }
}
