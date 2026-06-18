using System;

namespace Fluence.Core.Abstractions.Modules;

public interface IShellEvent { }

public interface IShellEventBus
{
    void Publish(IShellEvent shellEvent);
    void SubscribeSync<TEvent>(Action<TEvent> handler) where TEvent : IShellEvent;
    void UnsubscribeSync<TEvent>(Action<TEvent> handler) where TEvent : IShellEvent;
}

public sealed class ExpandPanelEvent(string panelId) : IShellEvent
{
    public string PanelId { get; } = panelId;
}

public sealed record SelectBottomBarTabEvent(string TabId) : IShellEvent;

public sealed record OpenFileRequestedEvent(string Path) : IShellEvent;

public sealed record OpenFileAtLocationRequestedEvent(string Path, int Line, int Character) : IShellEvent;

public sealed record OpenFolderRequestedEvent(string Path) : IShellEvent;

public sealed record OpenSolutionRequestedEvent(string Path) : IShellEvent;

public sealed record ManageNuGetPackagesRequestedEvent(string SolutionPath) : IShellEvent;

public sealed record NewProjectRequestedEvent : IShellEvent;

public sealed record PublishProjectRequestedEvent(string? ProjectPath = null) : IShellEvent;

public sealed record DotnetSdkSetupRequestedEvent : IShellEvent;

public sealed record DotnetSdkChangedEvent : IShellEvent;

public sealed record RefreshSolutionViewRequestedEvent : IShellEvent;

public sealed record SaveActiveDocumentRequestedEvent : IShellEvent;

public sealed record BuildWorkspaceRequestedEvent : IShellEvent;

public sealed record RestoreWorkspaceRequestedEvent : IShellEvent;

public sealed record CleanWorkspaceRequestedEvent : IShellEvent;

public sealed record RunProjectRequestedEvent : IShellEvent;

public sealed record DebugProjectRequestedEvent : IShellEvent;

public sealed record StopDebugRequestedEvent : IShellEvent;

public sealed record ReloadDebugRequestedEvent : IShellEvent;

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

public sealed record DebuggerProvisioningRequiredEvent : IShellEvent;
public sealed record DebuggerProvisioningFinishedEvent : IShellEvent;

public sealed record ActivityBarTabChangedEvent(string? TabId) : IShellEvent;

public sealed record ActivityBarTabSelectRequestedEvent(string? TabId) : IShellEvent;

// LSP — Editor → LanguageServer
public sealed record DocumentOpenedEvent(string FilePath, string Content, string LanguageId) : IShellEvent;
public sealed record DocumentChangedEvent(string FilePath, string Content, int Version) : IShellEvent;
public sealed record DocumentLiveChangedEvent(string FilePath, string Content, int Version, bool FlushImmediately) : IShellEvent;
public sealed record DocumentClosedEvent(string FilePath) : IShellEvent;
public sealed record GoToDefinitionRequestedEvent(string FilePath, int Line, int Character) : IShellEvent;
public sealed record GoToImplementationRequestedEvent(string FilePath, int Line, int Character) : IShellEvent;
public sealed record GoToTypeDefinitionRequestedEvent(string FilePath, int Line, int Character) : IShellEvent;
public sealed record LspInteractiveRequestStartedEvent(string FilePath) : IShellEvent;

// LSP — LanguageServer → Editor
public sealed record DiagnosticsUpdatedEvent(string FilePath, System.Collections.Generic.IReadOnlyList<Core.Models.LanguageServer.LspDiagnostic> Diagnostics) : IShellEvent;
public sealed record NavigationResolvedEvent(string FilePath, int Line, int Character) : IShellEvent;
public sealed record SemanticTokensUpdatedEvent(string FilePath, Core.Models.LanguageServer.SemanticToken[] Tokens) : IShellEvent;
public sealed record SemanticTokensRefreshStartedEvent(string FilePath) : IShellEvent;
public sealed record SemanticTokensRefreshFinishedEvent(string FilePath) : IShellEvent;
public sealed record SemanticTokensRefreshFailedEvent(string FilePath) : IShellEvent;

// LSP — provisioning lifecycle
public sealed record LspProvisioningRequiredEvent : IShellEvent;
public sealed record LspProvisioningCompletedEvent : IShellEvent;
public sealed record LspServerReadyEvent : IShellEvent;

// LSP — sync control
public sealed record FlushDocumentSyncEvent(string FilePath) : IShellEvent;

// LSP — workspace edits (server → editor, e.g. from workspace/applyEdit)
public sealed record WorkspaceEditRequestedEvent(string FilePath, System.Collections.Generic.IReadOnlyList<Core.Models.LanguageServer.LspTextEdit> Edits) : IShellEvent;

// Git
public sealed record GitCheckoutCompletedEvent(string BranchName) : IShellEvent;
