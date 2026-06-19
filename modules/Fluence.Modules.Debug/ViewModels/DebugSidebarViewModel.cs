using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Services.Debugging;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.ViewModels;
using Fluence.Modules.Debug.Models;
using Fluence.Core.Events.Debug;

namespace Fluence.Modules.Debug.ViewModels;

public sealed partial class DebugSidebarViewModel : ViewModelBase, IDisposable
{
    private readonly IDebugStateService _debugState;
    private readonly IShellEventBus _events;
    private readonly ILocalizationService _loc;

    [ObservableProperty]
    private string _sessionTitle;

    [ObservableProperty]
    private string _architecture = "x64";

    [ObservableProperty]
    private string _status;

    [ObservableProperty]
    private bool _canControlExecution;

    [ObservableProperty]
    private bool _canRestartSession;

    public DebugSidebarViewModel(IDebugStateService debugState, IShellEventBus events, ILocalizationService loc)
    {
        _debugState = debugState;
        _events = events;
        _loc = loc;
        _debugState.Changed += OnDebugStateChanged;
        _sessionTitle = _loc.Get("Debug.Session.NoSession");
        _status = _loc.Get("Debug.Status.Inactive");
        RefreshState();
    }

    public ObservableCollection<string> Variables { get; } = [];

    public ObservableCollection<string> StackFrames { get; } = [];

    public ObservableCollection<string> Breakpoints { get; } = [];

    public void Update(DebugSession session)
    {
        SessionTitle = session.IsActive
            ? _loc.Get("Debug.Session.Active")
            : _loc.Get("Debug.Session.NoSession");
        Architecture = session.TargetArchitecture;
        RefreshState();
    }

    public void Clear()
    {
        SessionTitle = _loc.Get("Debug.Session.NoSession");
        Architecture = "x64";
        RefreshState();
    }

    [RelayCommand]
    private void Continue() => _events.Publish(new ContinueDebugRequestedEvent());

    [RelayCommand]
    private void StepOver() => _events.Publish(new StepOverDebugRequestedEvent());

    [RelayCommand]
    private void StepInto() => _events.Publish(new StepIntoDebugRequestedEvent());

    [RelayCommand]
    private void StepOut() => _events.Publish(new StepOutDebugRequestedEvent());

    [RelayCommand]
    private void Stop() => _events.Publish(new StopDebugRequestedEvent());

    [RelayCommand]
    private void Reload() => _events.Publish(new ReloadDebugRequestedEvent());

    private void OnDebugStateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshState();
            return;
        }

        Dispatcher.UIThread.Post(RefreshState);
    }

    private void RefreshState()
    {
        var snapshot = _debugState.Snapshot;
        SessionTitle = snapshot.IsActive
            ? snapshot.IsStopped
                ? string.Format(_loc.Get("Debug.Session.Stopped"), snapshot.Reason ?? _loc.Get("Debug.Session.Breakpoint"))
                : _loc.Get("Debug.Session.Active")
            : SessionTitle;
        Status = snapshot.Status switch
        {
            DebugSessionStatus.Inactive => _loc.Get("Debug.Status.Inactive"),
            DebugSessionStatus.Starting => _loc.Get("Debug.Status.Starting"),
            DebugSessionStatus.Running => _loc.Get("Debug.Status.Running"),
            DebugSessionStatus.Stopped => _loc.Get("Debug.Status.Stopped"),
            DebugSessionStatus.Terminated => _loc.Get("Debug.Status.Terminated"),
            _ => snapshot.Status.ToString(),
        };
        CanControlExecution = snapshot.IsStopped && snapshot.ActiveThreadId is not null;
        CanRestartSession = snapshot.IsActive;

        Replace(Variables, snapshot.Variables.Select(FormatVariable).ToArray());

        Replace(StackFrames, snapshot.StackFrames.Select(FormatStackFrame).ToArray());
        Replace(Breakpoints, snapshot.Breakpoints.Select(FormatBreakpoint).ToArray());
    }

    private static string FormatVariable(DebugVariable variable)
    {
        var type = string.IsNullOrWhiteSpace(variable.Type) ? string.Empty : $" ({variable.Type})";
        return $"{variable.Name} = {variable.Value}{type}";
    }

    private static string FormatStackFrame(DebugStackFrame frame)
    {
        return $"{frame.Name}:{frame.Line}";
    }

    private string FormatBreakpoint(DebugBreakpoint breakpoint)
    {
        var status = breakpoint.IsVerified
            ? _loc.Get("Debug.Breakpoint.Verified")
            : string.IsNullOrWhiteSpace(breakpoint.Message)
                ? _loc.Get("Debug.Breakpoint.Pending")
                : breakpoint.Message;
        return $"{Path.GetFileName(breakpoint.FilePath)}:{breakpoint.Line} - {status}";
    }

    private static void Replace<T>(ObservableCollection<T> collection, T[] values)
    {
        collection.Clear();
        foreach (var value in values)
            collection.Add(value);
    }

    public void Dispose()
    {
        _debugState.Changed -= OnDebugStateChanged;
    }
}
