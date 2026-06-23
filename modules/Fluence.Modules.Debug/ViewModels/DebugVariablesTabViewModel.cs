using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Services.Debugging;
using Fluence.Core.ViewModels;

namespace Fluence.Modules.Debug.ViewModels;

public sealed class DebugVariablesTabViewModel : ViewModelBase, IDisposable
{
    private readonly IDebugStateService _debugState;
    private readonly IDebugService _debugService;
    private readonly ILocalizationService _loc;

    public DebugVariablesTabViewModel(
        IDebugStateService debugState,
        IDebugService debugService,
        ILocalizationService loc)
    {
        _debugState = debugState;
        _debugService = debugService;
        _loc = loc;
        _debugState.Changed += OnDebugStateChanged;
        Refresh();
    }

    public ObservableCollection<DebugVariableNode> RootVariables { get; } = [];

    public bool IsEmpty => RootVariables.Count == 0;

    private void OnDebugStateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
            return;
        }

        Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        var snapshot = _debugState.Snapshot;
        RootVariables.Clear();
        foreach (var variable in snapshot.Variables)
            RootVariables.Add(new DebugVariableNode(variable, _debugService.GetChildVariablesAsync, _loc));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void Dispose()
    {
        _debugState.Changed -= OnDebugStateChanged;
    }
}
