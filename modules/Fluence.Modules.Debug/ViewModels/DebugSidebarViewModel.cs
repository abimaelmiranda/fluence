using System.Collections.ObjectModel;
using ProcessArch = System.Runtime.InteropServices.Architecture;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Services.Debugging;
using Fluence.Core.ViewModels;
using Fluence.Modules.Debug.Models;

namespace Fluence.Modules.Debug.ViewModels;

public sealed partial class DebugSidebarViewModel : ViewModelBase, IDisposable
{
    private readonly IDebugStateService _debugState;
    private readonly IDebugService _debugService;
    private readonly ILocalizationService _loc;

    [ObservableProperty]
    private string _sessionTitle;

    [ObservableProperty]
    private string _architecture = GetDefaultArchitecture();

    [ObservableProperty]
    private string _status;

    [ObservableProperty]
    private bool _canControlExecution;

    [ObservableProperty]
    private bool _canRestartSession;

    public DebugSidebarViewModel(IDebugStateService debugState, IDebugService debugService, ILocalizationService loc)
    {
        _debugState = debugState;
        _debugService = debugService;
        _loc = loc;
        _debugState.Changed += OnDebugStateChanged;
        _sessionTitle = _loc.Get("Debug.Session.NoSession");
        _status = _loc.Get("Debug.Status.Inactive");
        RefreshState();
    }

    public ObservableCollection<string> StackFrames { get; } = [];

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
        Architecture = GetDefaultArchitecture();
        RefreshState();
    }

    [RelayCommand]
    private void Continue() => _ = _debugService.ContinueAsync();

    [RelayCommand]
    private void StepOver() => _ = _debugService.StepOverAsync();

    [RelayCommand]
    private void StepInto() => _ = _debugService.StepIntoAsync();

    [RelayCommand]
    private void StepOut() => _ = _debugService.StepOutAsync();

    [RelayCommand]
    private void Stop() => _ = _debugService.StopAsync();

    [RelayCommand]
    private void Reload() => _ = _debugService.RestartAsync();

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
        if (snapshot.IsActive && !string.IsNullOrWhiteSpace(snapshot.Architecture))
            Architecture = snapshot.Architecture;

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

        Replace(StackFrames, snapshot.StackFrames.Select(FormatStackFrame).ToArray());
    }

    private static string FormatStackFrame(DebugStackFrame frame) => $"{frame.Name}:{frame.Line}";

    private static void Replace<T>(ObservableCollection<T> collection, T[] values)
    {
        collection.Clear();
        foreach (var value in values)
            collection.Add(value);
    }

    private static string GetDefaultArchitecture() =>
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            ProcessArch.Arm64 => "arm64",
            ProcessArch.X64 => "x64",
            ProcessArch.X86 => "x86",
            _ => "x64",
        };

    public void Dispose()
    {
        _debugState.Changed -= OnDebugStateChanged;
    }
}
