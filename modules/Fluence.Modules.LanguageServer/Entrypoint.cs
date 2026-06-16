using System;
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

public sealed class Entrypoint : IModule, IDisposable
{
    private static readonly TimeSpan DidChangeDebounceDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan SemanticTokensDebounceDelay = TimeSpan.FromMilliseconds(800);

    private bool _provisioningPending;
    private string? _lastStartedRootPath;
    private ILanguageServerService? _languageServer;
    private SemanticTokensService? _semanticTokensService;
    private IShellEventBus? _eventBus;
    private readonly object _pendingDidChangeGate = new();
    private PendingDidChange? _pendingDidChange;
    private DateTime? _pendingDidChangeDueAtUtc;
    private Timer? _pendingDidChangeTimer;
    private Task? _pendingDidChangeSendTask;

    private readonly object _pendingSemanticGate = new();
    private string? _pendingSemanticFilePath;
    private Timer? _pendingSemanticTimer;

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
        host.Events.Subscribe<DocumentOpenedEvent>(e =>
        {
            if (!lsp.IsRunning) return;
            SafeSend(lsp.SendDidOpenAsync(e.FilePath, e.LanguageId, e.Content, CancellationToken.None));
            QueueSemanticTokens(e.FilePath);
        });

        host.Events.Subscribe<DocumentChangedEvent>(e =>
        {
            if (!lsp.IsRunning) return;
            QueueDidChange(e.FilePath, e.Version, e.Content);
            QueueSemanticTokens(e.FilePath);
        });

        host.Events.Subscribe<DocumentClosedEvent>(e =>
        {
            if (!lsp.IsRunning) return;
            SafeSend(FlushPendingDidChangeAndCloseAsync(lsp, e.FilePath));
        });

        // Use diagnostics as a signal that OmniSharp finished analyzing — safe moment to fetch semantic tokens.
        // This also covers the startup case where the LSP wasn't running when DocumentOpenedEvent fired.
        host.Events.Subscribe<DiagnosticsUpdatedEvent>(e =>
        {
            if (!lsp.IsRunning) return;
            QueueSemanticTokens(e.FilePath);
        });

        host.Events.Subscribe<FlushDocumentSyncEvent>(e =>
        {
            if (!lsp.IsRunning) return;
            SafeSend(FlushPendingDidChangeImmediatelyAsync(lsp, e.FilePath));
        });

        // Navigation requests — resolve and open destination
        host.Events.Subscribe<GoToDefinitionRequestedEvent>(e => HandleNavigation(host, lsp, "definition", e.FilePath, e.Line, e.Character));
        host.Events.Subscribe<GoToImplementationRequestedEvent>(e => HandleNavigation(host, lsp, "implementation", e.FilePath, e.Line, e.Character));
        host.Events.Subscribe<GoToTypeDefinitionRequestedEvent>(e => HandleNavigation(host, lsp, "typeDefinition", e.FilePath, e.Line, e.Character));

        // Provisioning completed — start the server (or reset guard if it failed)
        host.Events.Subscribe<LspProvisioningCompletedEvent>(_ =>
        {
            _provisioningPending = false;
            TryStartOrRestart(host, lsp, provisioning, fromProvisioning: true);
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
        _ = lsp.StartAsync(rootPath, CancellationToken.None);
    }

    private static void HandleNavigation(IModuleHost host, ILanguageServerService lsp, string kind, string filePath, int line, int character)
    {
        var nav = host.Services.GetRequiredService<INavigationService>();
        _ = ResolveAndPublishNavigation(host, nav, kind, filePath, line, character);
    }

    private static async System.Threading.Tasks.Task ResolveAndPublishNavigation(
        IModuleHost host,
        INavigationService nav,
        string kind,
        string filePath,
        int line,
        int character)
    {
        try
        {
            var location = kind switch
            {
                "definition" => await nav.GetDefinitionAsync(filePath, line, character),
                "implementation" => await nav.GetImplementationAsync(filePath, line, character),
                "typeDefinition" => await nav.GetTypeDefinitionAsync(filePath, line, character),
                _ => null,
            };

            if (location is null)
                return;

            // Open the file first, then navigate to the position
            if (!string.Equals(location.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                host.Events.Publish(new OpenFileRequestedEvent(location.FilePath));

            host.Events.Publish(new NavigationResolvedEvent(location.FilePath, location.Line, location.Character));
        }
        catch { }
    }

    private static void SafeSend(Task task) =>
        task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

    private void QueueDidChange(string filePath, int version, string content)
    {
        lock (_pendingDidChangeGate)
        {
            _pendingDidChange = new PendingDidChange(filePath, content, version);
            _pendingDidChangeDueAtUtc = DateTime.UtcNow + DidChangeDebounceDelay;
            _pendingDidChangeTimer ??= new Timer(
                static state => ((Entrypoint)state!).OnDidChangeTimerElapsed(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _pendingDidChangeTimer.Change(DidChangeDebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task FlushPendingDidChangeImmediatelyAsync(ILanguageServerService lsp, string filePath)
    {
        PendingDidChange? pending;
        Task? inFlightTask;
        lock (_pendingDidChangeGate)
        {
            _pendingDidChangeTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            pending = _pendingDidChange is not null &&
                      string.Equals(_pendingDidChange.FilePath, filePath, StringComparison.OrdinalIgnoreCase)
                ? _pendingDidChange
                : null;

            if (pending is not null)
            {
                _pendingDidChange = null;
                _pendingDidChangeDueAtUtc = null;
            }

            inFlightTask = _pendingDidChangeSendTask;
        }

        if (inFlightTask is not null)
        {
            try { await inFlightTask.ConfigureAwait(false); }
            catch { }
        }

        if (pending is not null)
        {
            try
            {
                await lsp.SendDidChangeAsync(pending.FilePath, pending.Version, pending.Content, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch { }
        }
    }

    private async Task FlushPendingDidChangeAndCloseAsync(ILanguageServerService lsp, string filePath)
    {
        PendingDidChange? pending;
        Task? inFlightTask;
        lock (_pendingDidChangeGate)
        {
            _pendingDidChangeTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            pending = _pendingDidChange is not null &&
                      string.Equals(_pendingDidChange.FilePath, filePath, StringComparison.OrdinalIgnoreCase)
                ? _pendingDidChange
                : null;

            if (pending is not null)
            {
                _pendingDidChange = null;
                _pendingDidChangeDueAtUtc = null;
            }

            inFlightTask = _pendingDidChangeSendTask;
        }

        if (inFlightTask is not null)
        {
            try
            {
                await inFlightTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }

        if (pending is not null)
        {
            try
            {
                await lsp.SendDidChangeAsync(pending.FilePath, pending.Version, pending.Content, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }

        try
        {
            await lsp.SendDidCloseAsync(filePath, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void OnDidChangeTimerElapsed()
    {
        TimeSpan remaining;
        lock (_pendingDidChangeGate)
        {
            if (_pendingDidChange is null || _pendingDidChangeDueAtUtc is null)
                return;

            remaining = _pendingDidChangeDueAtUtc.Value - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                _pendingDidChangeTimer?.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }

            var pending = _pendingDidChange;
            _pendingDidChange = null;
            _pendingDidChangeDueAtUtc = null;

            if (pending is null || _languageServer is null)
                return;

            _pendingDidChangeSendTask = SendPendingDidChangeAsync(_languageServer, pending);
        }
    }

    private async Task SendPendingDidChangeAsync(ILanguageServerService lsp, PendingDidChange pending)
    {
        try
        {
            await lsp.SendDidChangeAsync(pending.FilePath, pending.Version, pending.Content, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void QueueSemanticTokens(string filePath)
    {
        lock (_pendingSemanticGate)
        {
            _pendingSemanticFilePath = filePath;
            _pendingSemanticTimer ??= new Timer(
                static state => ((Entrypoint)state!).OnSemanticTokensTimerElapsed(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _pendingSemanticTimer.Change(SemanticTokensDebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnSemanticTokensTimerElapsed()
    {
        string? filePath;
        lock (_pendingSemanticGate)
        {
            filePath = _pendingSemanticFilePath;
            _pendingSemanticFilePath = null;
        }

        if (filePath is null || _semanticTokensService is null || _eventBus is null) return;
        SafeSend(SendSemanticTokensAsync(_semanticTokensService, _eventBus, filePath));
    }

    private static async Task SendSemanticTokensAsync(SemanticTokensService service, IShellEventBus events, string filePath)
    {
        try
        {
            var tokens = await service.RequestAsync(filePath, CancellationToken.None).ConfigureAwait(false);
            if (tokens.Length > 0)
                events.Publish(new SemanticTokensUpdatedEvent(filePath, tokens));
        }
        catch { }
    }

    private static string? ResolveRootPath(IWorkspaceContext workspace)
    {
        var current = workspace.Current;
        if (current.Mode == WorkspaceMode.Solution && !string.IsNullOrWhiteSpace(current.CurrentSolutionPath))
            return Path.GetDirectoryName(current.CurrentSolutionPath);
        if (current.Mode == WorkspaceMode.Folder && !string.IsNullOrWhiteSpace(current.CurrentFolderPath))
            return current.CurrentFolderPath;
        return null;
    }

    private sealed record PendingDidChange(string FilePath, string Content, int Version);
}
