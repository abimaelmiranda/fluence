using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Debug;
using Fluence.Core.Modules;
using Fluence.Core.ViewModels;

namespace Fluence.Modules.Debug.ViewModels;

public sealed partial class DebugSidebarViewModel : ViewModelBase, IDisposable
{
    private readonly IDebugStateService _debugState;
    private readonly IShellEventBus _events;

    [ObservableProperty]
    private string _sessionTitle = "No debug session";

    [ObservableProperty]
    private string _architecture = "x64";

    [ObservableProperty]
    private string _status = "Inactive";

    [ObservableProperty]
    private bool _canControlExecution;

    [ObservableProperty]
    private bool _canRestartSession;

    public DebugSidebarViewModel(IDebugStateService debugState, IShellEventBus events)
    {
        _debugState = debugState;
        _events = events;
        _debugState.Changed += OnDebugStateChanged;
        RefreshState();
    }

    public ObservableCollection<string> Variables { get; } = [];

    public ObservableCollection<string> StackFrames { get; } = [];

    public ObservableCollection<string> Breakpoints { get; } = [];

    public void Update(DebugSession session)
    {
        SessionTitle = session.IsActive ? "Debug session active" : "No debug session";
        Architecture = session.TargetArchitecture;
        RefreshState();
    }

    public void Clear()
    {
        SessionTitle = "No debug session";
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
            ? snapshot.IsStopped ? $"Stopped: {snapshot.Reason ?? "breakpoint"}" : "Debug session active"
            : SessionTitle;
        Status = snapshot.Status.ToString();
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

    private static string FormatBreakpoint(DebugBreakpoint breakpoint)
    {
        var status = breakpoint.IsVerified
            ? "Verified"
            : string.IsNullOrWhiteSpace(breakpoint.Message)
                ? "Pending"
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
