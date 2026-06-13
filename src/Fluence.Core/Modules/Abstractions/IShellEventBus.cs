using System;

namespace Fluence.Core.Modules;

public interface IShellEvent { }

public interface IShellEventBus
{
    void Publish(IShellEvent shellEvent);
    void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IShellEvent;
    void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : IShellEvent;
}

public sealed class ExpandPanelEvent(string panelId) : IShellEvent
{
    public string PanelId { get; } = panelId;
}

public sealed record OpenFileRequestedEvent(string Path) : IShellEvent;

public sealed record OpenFolderRequestedEvent(string Path) : IShellEvent;

public sealed record OpenSolutionRequestedEvent(string Path) : IShellEvent;

public sealed record ManageNuGetPackagesRequestedEvent(string SolutionPath) : IShellEvent;

public sealed record RefreshSolutionViewRequestedEvent : IShellEvent;

public sealed record SaveActiveDocumentRequestedEvent : IShellEvent;

public sealed record BuildWorkspaceRequestedEvent : IShellEvent;

public sealed record RestoreWorkspaceRequestedEvent : IShellEvent;

public sealed record CleanWorkspaceRequestedEvent : IShellEvent;

public sealed record RunProjectRequestedEvent : IShellEvent;

public sealed record DebugProjectRequestedEvent : IShellEvent;

public sealed record StopDebugRequestedEvent : IShellEvent;

public sealed record ContinueDebugRequestedEvent : IShellEvent;

public sealed record StepOverDebugRequestedEvent : IShellEvent;

public sealed record StepIntoDebugRequestedEvent : IShellEvent;

public sealed record StepOutDebugRequestedEvent : IShellEvent;

public sealed record ToggleBreakpointRequestedEvent(string FilePath, int Line) : IShellEvent;

public sealed record TestWorkspaceRequestedEvent : IShellEvent;

public sealed record BuildProjectRequestedEvent(string ProjectPath) : IShellEvent;

public sealed record RestoreProjectRequestedEvent(string ProjectPath) : IShellEvent;

public sealed record CleanProjectRequestedEvent(string ProjectPath) : IShellEvent;

public sealed record RunSpecificProjectRequestedEvent(string ProjectPath) : IShellEvent;

public sealed record TestProjectRequestedEvent(string ProjectPath) : IShellEvent;
