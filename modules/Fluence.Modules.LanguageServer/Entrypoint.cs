using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Modules.LanguageServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.LanguageServer;

public sealed partial class Entrypoint : IModule, IDisposable
{
    private static readonly TimeSpan DidChangeDebounceDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan SemanticTokensDebounceDelay = TimeSpan.FromMilliseconds(800);

    private readonly object _documentGate = new();
    private readonly Dictionary<string, DocumentSyncState> _documents = new(StringComparer.OrdinalIgnoreCase);
    private bool _provisioningPending;
    private string? _lastStartedRootPath;
    private ILanguageServerService? _languageServer;
    private SemanticTokensService? _semanticTokensService;
    private IShellEventBus? _eventBus;
    private ITaskScheduler? _scheduler;

    public string Name => "LanguageServer";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<LspClientHolder>();
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        services.AddSingleton<ILanguageServerService>(provider =>
            new LanguageServerService(
                provider.GetRequiredService<ILspProvisioningService>(),
                provider.GetRequiredService<IDiagnosticsService>(),
                provider.GetRequiredService<IShellEventBus>(),
                provider.GetRequiredService<LspClientHolder>()));
        services.AddSingleton<ICompletionService>(provider =>
            new CompletionService(
                provider.GetRequiredService<ILanguageServerService>(),
                provider.GetRequiredService<LspClientHolder>()));
        services.AddSingleton<INavigationService>(provider =>
            new NavigationService(
                provider.GetRequiredService<ILanguageServerService>(),
                provider.GetRequiredService<LspClientHolder>()));
        services.AddSingleton<IHoverService>(provider =>
            new HoverService(
                provider.GetRequiredService<ILanguageServerService>(),
                provider.GetRequiredService<LspClientHolder>()));
        services.AddSingleton<ISignatureHelpService>(provider =>
            new SignatureHelpService(
                provider.GetRequiredService<ILanguageServerService>(),
                provider.GetRequiredService<LspClientHolder>()));
        services.AddSingleton<ICodeActionService>(provider =>
            new CodeActionService(
                provider.GetRequiredService<ILanguageServerService>(),
                provider.GetRequiredService<LspClientHolder>()));
        services.AddSingleton<IFormattingService>(provider =>
            new FormattingService(
                provider.GetRequiredService<ILanguageServerService>(),
                provider.GetRequiredService<LspClientHolder>()));
        services.AddSingleton<SemanticTokensService>(provider =>
            new SemanticTokensService(
                (LanguageServerService)provider.GetRequiredService<ILanguageServerService>(),
                provider.GetRequiredService<LspClientHolder>()));
    }

    public void Dispose()
    {
    }

    public void Initialize(IModuleHost host)
    {
        var lsp = host.Services.GetRequiredService<ILanguageServerService>();
        var provisioning = host.Services.GetRequiredService<ILspProvisioningService>();
        var nav = host.Services.GetRequiredService<INavigationService>();
        _languageServer = lsp;
        _semanticTokensService = host.Services.GetRequiredService<SemanticTokensService>();
        _eventBus = host.Events;
        _scheduler = host.Services.GetRequiredService<ITaskScheduler>();

        host.Workspace.Changed += (_, _) => TryStartOrRestart(host, lsp, provisioning, fromProvisioning: false);

        host.Events.SubscribeSync<DocumentOpenedEvent>(e =>
        {
            RegisterDocument(e.FilePath, e.Content, e.LanguageId, version: 1);
            if (!lsp.IsRunning) return;
            _scheduler.Schedule(
                $"lsp.ensure-open.{e.FilePath}",
                TaskPriority.Interactive,
                ct => EnsureDocumentOpenAndQueueSemanticTokensAsync(lsp, e.FilePath, ct),
                correlationId: e.FilePath);
        });

        host.Events.SubscribeSync<DocumentChangedEvent>(e =>
        {
            HandleDocumentContentChanged(lsp, e.FilePath, e.Content, e.Version, flushImmediately: false);
        });

        host.Events.SubscribeSync<DocumentLiveChangedEvent>(e =>
        {
            HandleDocumentContentChanged(lsp, e.FilePath, e.Content, e.Version, e.FlushImmediately);
        });

        host.Events.SubscribeSync<DocumentClosedEvent>(e =>
        {
            UnregisterDocument(e.FilePath);
            if (!lsp.IsRunning) return;
            _scheduler.Schedule(
                $"lsp.close.{e.FilePath}",
                TaskPriority.Interactive,
                ct => FlushPendingDidChangeAndCloseAsync(lsp, e.FilePath, ct));
        });

        host.Events.SubscribeSync<LspServerReadyEvent>(_ =>
        {
            foreach (var path in SnapshotDocumentPaths())
                _scheduler.Schedule(
                    $"lsp.ensure-open.{path}",
                    TaskPriority.Interactive,
                    ct => EnsureDocumentOpenAndQueueSemanticTokensAsync(lsp, path, ct),
                    correlationId: path);
        });

        // Diagnostics signal OmniSharp finished analyzing — safe moment to fetch semantic tokens
        host.Events.SubscribeSync<DiagnosticsUpdatedEvent>(e =>
        {
            if (!lsp.IsRunning) return;
            QueueSemanticTokens(e.FilePath);
        });

        host.Events.SubscribeSync<FlushDocumentSyncEvent>(e =>
        {
            if (!lsp.IsRunning) return;
            _scheduler.Schedule(
                $"lsp.flush.{e.FilePath}",
                TaskPriority.Critical,
                ct => FlushPendingDidChangeImmediatelyAsync(lsp, e.FilePath, ct));
        });

        host.Events.SubscribeSync<LspInteractiveRequestStartedEvent>(e =>
        {
            CancelSemanticTokens(e.FilePath);
        });

        host.Events.SubscribeSync<GoToDefinitionRequestedEvent>(e =>
            _scheduler.Schedule("lsp.navigation", TaskPriority.Interactive,
                ct => HandleNavigation(host, nav, "definition", e.FilePath, e.Line, e.Character, ct),
                correlationId: $"{e.FilePath}:{e.Line}:{e.Character}"));

        host.Events.SubscribeSync<GoToImplementationRequestedEvent>(e =>
            _scheduler.Schedule("lsp.navigation", TaskPriority.Interactive,
                ct => HandleNavigation(host, nav, "implementation", e.FilePath, e.Line, e.Character, ct),
                correlationId: $"{e.FilePath}:{e.Line}:{e.Character}"));

        host.Events.SubscribeSync<GoToTypeDefinitionRequestedEvent>(e =>
            _scheduler.Schedule("lsp.navigation", TaskPriority.Interactive,
                ct => HandleNavigation(host, nav, "typeDefinition", e.FilePath, e.Line, e.Character, ct),
                correlationId: $"{e.FilePath}:{e.Line}:{e.Character}"));

        host.Events.SubscribeSync<LspProvisioningCompletedEvent>(_ =>
        {
            _provisioningPending = false;
            TryStartOrRestart(host, lsp, provisioning, fromProvisioning: true);
        });

        host.SetModuleState(Name, ModuleState.Active);
    }

    private void HandleDocumentContentChanged(
        ILanguageServerService lsp,
        string filePath,
        string content,
        int version,
        bool flushImmediately)
    {
        RegisterDocument(filePath, content, "csharp", version);
        if (!lsp.IsRunning) return;

        if (IsOpenInServer(filePath))
        {
            QueueDidChange(filePath, version, content);
            if (flushImmediately)
            {
                _scheduler!.Schedule(
                    $"lsp.flush.{filePath}",
                    TaskPriority.Critical,
                    ct => FlushPendingDidChangeImmediatelyAsync(lsp, filePath, ct),
                    correlationId: version);
            }
            else
            {
                QueueSemanticTokens(filePath);
            }
        }
        else
        {
            _scheduler!.Schedule(
                $"lsp.ensure-open.{filePath}",
                TaskPriority.Interactive,
                ct => EnsureDocumentOpenAndQueueSemanticTokensAsync(lsp, filePath, ct),
                correlationId: version);
        }
    }

    private void TryStartOrRestart(IModuleHost host, ILanguageServerService lsp, ILspProvisioningService provisioning, bool fromProvisioning)
    {
        var rootPath = ResolveRootPath(host.Workspace);
        if (rootPath is null)
        {
            if (_lastStartedRootPath is not null)
            {
                _lastStartedRootPath = null;
                ResetServerDocumentState();
                _ = lsp.StopAsync();
            }
            return;
        }

        if (!provisioning.IsProvisioned())
        {
            // Only publish provisioning event once — opening the setup tab triggers
            // Workspace.Changed again, which would cause an infinite loop without this guard.
            if (!_provisioningPending)
            {
                _provisioningPending = true;
                host.Events.Publish(new LspProvisioningRequiredEvent());
            }
            return;
        }

        _provisioningPending = false;

        // Avoid restarting when workspace changes are caused by things unrelated to the root
        // (e.g. tab switches, content updates). Only (re)start when root path actually changes
        // or when provisioning just completed.
        if (!fromProvisioning && string.Equals(rootPath, _lastStartedRootPath, StringComparison.OrdinalIgnoreCase))
            return;

        _lastStartedRootPath = rootPath;
        ResetServerDocumentState();
        _scheduler!.Schedule(
            "lsp.start",
            TaskPriority.Interactive,
            ct => lsp.StartAsync(rootPath, ct),
            correlationId: rootPath);
    }

    private void RegisterDocument(string filePath, string content, string languageId, int version)
    {
        if (!IsCSharpDocument(filePath, languageId))
            return;

        lock (_documentGate)
        {
            if (_documents.TryGetValue(filePath, out var current))
            {
                current.Content = content;
                current.LanguageId = languageId;
                current.Version = version;
                current.TokensPending = true;
                current.SemanticAttempt = 0;
            }
            else
            {
                _documents[filePath] = new DocumentSyncState(content, languageId, version)
                {
                    TokensPending = true,
                };
            }
        }
    }

    private void UnregisterDocument(string filePath)
    {
        lock (_documentGate)
            _documents.Remove(filePath);

        _eventBus?.Publish(new SemanticTokensRefreshFinishedEvent(filePath));
    }

    private bool IsOpenInServer(string filePath)
    {
        lock (_documentGate)
            return _documents.TryGetValue(filePath, out var state) && state.IsOpenInServer;
    }

    private string[] SnapshotDocumentPaths()
    {
        lock (_documentGate)
        {
            var paths = new string[_documents.Count];
            _documents.Keys.CopyTo(paths, 0);
            return paths;
        }
    }

    private void ResetServerDocumentState()
    {
        lock (_documentGate)
        {
            foreach (var state in _documents.Values)
            {
                state.IsOpenInServer = false;
                state.TokensPending = true;
                state.SemanticAttempt = 0;
            }
        }
    }

    private async Task EnsureDocumentOpenAndQueueSemanticTokensAsync(ILanguageServerService lsp, string filePath, CancellationToken ct)
    {
        DocumentSyncState? snapshot;
        lock (_documentGate)
        {
            if (!_documents.TryGetValue(filePath, out var state))
                return;

            if (state.IsOpenInServer)
            {
                QueueSemanticTokens(filePath);
                return;
            }

            snapshot = state.Clone();
        }

        await lsp.SendDidOpenAsync(
            filePath,
            snapshot.LanguageId,
            snapshot.Content,
            ct).ConfigureAwait(false);

        lock (_documentGate)
        {
            if (_documents.TryGetValue(filePath, out var state))
                state.IsOpenInServer = true;
        }

        QueueSemanticTokens(filePath);
    }

    private static bool IsCSharpDocument(string filePath, string languageId) =>
        string.Equals(languageId, "csharp", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveRootPath(IWorkspaceContext workspace)
    {
        var current = workspace.Current;
        if (current.NavigationMode == WorkspaceMode.Solution && !string.IsNullOrWhiteSpace(current.CurrentSolutionPath))
            return Path.GetDirectoryName(current.CurrentSolutionPath);
        if (current.NavigationMode == WorkspaceMode.Folder && !string.IsNullOrWhiteSpace(current.CurrentFolderPath))
            return current.CurrentFolderPath;
        return null;
    }

    private sealed class DocumentSyncState(string content, string languageId, int version)
    {
        public string Content { get; set; } = content;
        public string LanguageId { get; set; } = languageId;
        public int Version { get; set; } = version;
        public bool IsOpenInServer { get; set; }
        public bool TokensPending { get; set; }
        public int SemanticAttempt { get; set; }

        public DocumentSyncState Clone() => new(Content, LanguageId, Version)
        {
            IsOpenInServer = IsOpenInServer,
            TokensPending = TokensPending,
            SemanticAttempt = SemanticAttempt,
        };
    }
}
