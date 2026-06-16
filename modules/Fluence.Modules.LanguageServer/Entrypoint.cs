using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Modules;
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
        services.AddSingleton<SemanticTokensService>(provider =>
            new SemanticTokensService(
                (LanguageServerService)provider.GetRequiredService<ILanguageServerService>(),
                provider.GetRequiredService<LspClientHolder>()));
    }

    public void Dispose()
    {
        _pendingDidChangeTimer?.Dispose();
        _pendingSemanticTimer?.Dispose();
    }

    public void Initialize(IModuleHost host)
    {
        var lsp = host.Services.GetRequiredService<ILanguageServerService>();
        var provisioning = host.Services.GetRequiredService<ILspProvisioningService>();
        _languageServer = lsp;
        _semanticTokensService = host.Services.GetRequiredService<SemanticTokensService>();
        _eventBus = host.Events;

        // Start language server when workspace has a root
        host.Workspace.Changed += (_, _) => TryStartOrRestart(host, lsp, provisioning, fromProvisioning: false);

        // Respond to document lifecycle events from the editor
        host.Events.SubscribeAsync<DocumentOpenedEvent>(e =>
        {
            RegisterDocument(e.FilePath, e.Content, e.LanguageId, version: 1, publishStarted: true);
            if (lsp.IsRunning)
                SafeSend(EnsureDocumentOpenAndQueueSemanticTokensAsync(lsp, e.FilePath));
            return Task.CompletedTask;
        });

        host.Events.SubscribeAsync<DocumentChangedEvent>(e =>
        {
            RegisterDocument(e.FilePath, e.Content, "csharp", e.Version, publishStarted: true);
            if (!lsp.IsRunning) return Task.CompletedTask;

            if (IsOpenInServer(e.FilePath))
            {
                QueueDidChange(e.FilePath, e.Version, e.Content);
                QueueSemanticTokens(e.FilePath);
            }
            else
            {
                SafeSend(EnsureDocumentOpenAndQueueSemanticTokensAsync(lsp, e.FilePath));
            }

            return Task.CompletedTask;
        });

        host.Events.SubscribeAsync<DocumentClosedEvent>(e =>
        {
            UnregisterDocument(e.FilePath);
            if (!lsp.IsRunning) return Task.CompletedTask;
            SafeSend(FlushPendingDidChangeAndCloseAsync(lsp, e.FilePath));
            return Task.CompletedTask;
        });

        // DocumentOpenedEvent may fire before the server is running, especially during snapshot restore.
        // Once ready, replay tracked documents into the LSP and request semantic tokens.
        host.Events.SubscribeAsync<LspServerReadyEvent>(_ =>
        {
            foreach (var path in SnapshotDocumentPaths())
                SafeSend(EnsureDocumentOpenAndQueueSemanticTokensAsync(lsp, path));
            return Task.CompletedTask;
        });

        // Use diagnostics as a signal that OmniSharp finished analyzing — safe moment to fetch semantic tokens.
        // This also covers the startup case where the LSP wasn't running when DocumentOpenedEvent fired.
        host.Events.SubscribeAsync<DiagnosticsUpdatedEvent>(e =>
        {
            if (!lsp.IsRunning) return Task.CompletedTask;
            QueueSemanticTokens(e.FilePath);
            return Task.CompletedTask;
        });

        host.Events.SubscribeAsync<FlushDocumentSyncEvent>(e =>
        {
            if (!lsp.IsRunning) return Task.CompletedTask;
            SafeSend(FlushPendingDidChangeImmediatelyAsync(lsp, e.FilePath));
            return Task.CompletedTask;
        });

        // Navigation requests — resolve and open destination
        host.Events.SubscribeAsync<GoToDefinitionRequestedEvent>(e => HandleNavigation(host, lsp, "definition", e.FilePath, e.Line, e.Character));
        host.Events.SubscribeAsync<GoToImplementationRequestedEvent>(e => HandleNavigation(host, lsp, "implementation", e.FilePath, e.Line, e.Character));
        host.Events.SubscribeAsync<GoToTypeDefinitionRequestedEvent>(e => HandleNavigation(host, lsp, "typeDefinition", e.FilePath, e.Line, e.Character));

        // Provisioning completed — start the server (or reset guard if it failed)
        host.Events.SubscribeAsync<LspProvisioningCompletedEvent>(_ =>
        {
            _provisioningPending = false;
            TryStartOrRestart(host, lsp, provisioning, fromProvisioning: true);
            return Task.CompletedTask;
        });

        host.SetModuleState(Name, ModuleState.Active);
    }

    private void TryStartOrRestart(IModuleHost host, ILanguageServerService lsp, ILspProvisioningService provisioning, bool fromProvisioning)
    {
        var rootPath = ResolveRootPath(host.Workspace);
        if (rootPath is null)
        {
            if (_lastStartedRootPath is not null)
            {
                _lastStartedRootPath = null;
                ResetServerDocumentState(publishStarted: false);
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

        // Reset guard so future "not provisioned" states work again
        _provisioningPending = false;

        // Avoid restarting when workspace changes are caused by things unrelated to the root
        // (e.g. tab switches, content updates). Only (re)start when root path actually changes
        // or when provisioning just completed.
        if (!fromProvisioning && string.Equals(rootPath, _lastStartedRootPath, StringComparison.OrdinalIgnoreCase))
            return;

        _lastStartedRootPath = rootPath;
        ResetServerDocumentState(publishStarted: true);
        _ = lsp.StartAsync(rootPath, CancellationToken.None);
    }

    private void RegisterDocument(string filePath, string content, string languageId, int version, bool publishStarted)
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

        if (publishStarted)
            _eventBus?.Publish(new SemanticTokensRefreshStartedEvent(filePath));
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

    private void ResetServerDocumentState(bool publishStarted)
    {
        string[] pendingPaths;
        lock (_documentGate)
        {
            foreach (var state in _documents.Values)
            {
                state.IsOpenInServer = false;
                state.TokensPending = true;
                state.SemanticAttempt = 0;
            }

            pendingPaths = [.. _documents.Keys];
        }

        if (!publishStarted || _eventBus is null)
            return;

        foreach (var path in pendingPaths)
            _eventBus.Publish(new SemanticTokensRefreshStartedEvent(path));
    }

    private async Task EnsureDocumentOpenAndQueueSemanticTokensAsync(ILanguageServerService lsp, string filePath)
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
            CancellationToken.None).ConfigureAwait(false);

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
        if (current.Mode == WorkspaceMode.Solution && !string.IsNullOrWhiteSpace(current.CurrentSolutionPath))
            return Path.GetDirectoryName(current.CurrentSolutionPath);
        if (current.Mode == WorkspaceMode.Folder && !string.IsNullOrWhiteSpace(current.CurrentFolderPath))
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
