using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Problems;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.LanguageServer;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Problems;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Modules.LanguageServer.Json;
using Fluence.Modules.LanguageServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.LanguageServer;

public sealed partial class Entrypoint : IModule, IModuleShutdownParticipant
{
    private static readonly TimeSpan DidChangeDebounceDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan SemanticTokensDebounceDelay = TimeSpan.FromMilliseconds(800);

    private readonly object _documentGate = new();
    private readonly Dictionary<string, DocumentSyncState> _documents = new(StringComparer.OrdinalIgnoreCase);
    private bool _provisioningPending;
    private int _diagnosticsPublishedOpenFileCount;
    private string? _lastStartedRootPath;
    private ILanguageServerService? _languageServer;
    private SemanticTokensService? _semanticTokensService;
    private IShellEventBus? _eventBus;
    private ITaskScheduler? _scheduler;
    private IWorkspaceContext? _workspace;
    private EventHandler? _workspaceChanged;
    private readonly List<IDisposable> _subscriptions = [];

    public string Name => "LanguageServer";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<LspClientHolder>();
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        services.AddSingleton<ILanguageServerService>(provider =>
            new LanguageServerService(
                provider.GetRequiredService<ILspProvisioningService>(),
                provider.GetRequiredService<IProcessSpawner>(),
                provider.GetRequiredService<IDiagnosticsService>(),
                provider.GetRequiredService<IShellEventBus>(),
                provider.GetRequiredService<IOutputChannelService>(),
                provider.GetRequiredService<ISettingsService>(),
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

    public async ValueTask DisposeAsync()
    {
        _scheduler?.CancelAndForget("lsp.start");
        _scheduler?.CancelAndForget("lsp.problems");
        _scheduler?.CancelAndForget("lsp.diagnostics-output");

        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        _subscriptions.Clear();

        if (_workspace is not null && _workspaceChanged is not null)
            _workspace.Changed -= _workspaceChanged;
        _workspaceChanged = null;
        _workspace = null;

        foreach (var path in SnapshotDocumentPaths())
            UnregisterDocument(path);

        if (_languageServer is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);

        _languageServer = null;
        _semanticTokensService = null;
        _eventBus = null;
        _scheduler = null;
    }

    public async Task StopAsync(ModuleShutdownContext context)
    {
        _scheduler?.CancelAndForget("lsp.start");
        _scheduler?.CancelAndForget("lsp.problems");
        _scheduler?.CancelAndForget("lsp.diagnostics-output");

        var languageServer = _languageServer;
        _languageServer = null;
        if (languageServer is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().AsTask()
                .WaitAsync(context.CancellationToken)
                .ConfigureAwait(false);
    }

    public void Initialize(IModuleHost host)
    {
        host.Services.GetRequiredService<ISettingsRegistry>()
            .Register(LanguageServerSettingsJsonContext.Default.LanguageServerSettings);
        host.Services.GetRequiredService<ISettingsService>()
            .Get<LanguageServerSettings>();

        var lsp = host.Services.GetRequiredService<ILanguageServerService>();
        var provisioning = host.Services.GetRequiredService<ILspProvisioningService>();
        var nav = host.Services.GetRequiredService<INavigationService>();
        var diagnosticsService = host.Services.GetRequiredService<IDiagnosticsService>();
        var problems = host.Services.GetRequiredService<IProblemService>();
        var output = host.Services.GetRequiredService<IOutputChannelService>();
        _languageServer = lsp;
        _semanticTokensService = host.Services.GetRequiredService<SemanticTokensService>();
        _eventBus = host.Events;
        _scheduler = host.Services.GetRequiredService<ITaskScheduler>();
        _workspace = host.Workspace;

        _workspaceChanged = (_, _) => TryStartOrRestart(host, lsp, provisioning, output, fromProvisioning: false);
        host.Workspace.Changed += _workspaceChanged;

        _subscriptions.Add(host.Events.SubscribeSync<DocumentOpenedEvent>(e =>
        {
            RegisterDocument(e.FilePath, e.Content, e.LanguageId, e.Version);
            PublishCachedDiagnosticsForOpenDocument(diagnosticsService, problems, output, e.FilePath);
            if (!lsp.IsRunning) return;
            _scheduler.Schedule(
                $"lsp.ensure-open.{e.FilePath}",
                TaskPriority.Interactive,
                ct => EnsureDocumentOpenAndQueueSemanticTokensAsync(lsp, e.FilePath, ct),
                correlationId: e.FilePath);
        }));

        _subscriptions.Add(host.Events.SubscribeSync<DocumentChangedEvent>(e =>
        {
            HandleDocumentContentChanged(lsp, e.FilePath, e.Content, e.Version, flushImmediately: false);
        }));

        _subscriptions.Add(host.Events.SubscribeSync<DocumentLiveChangedEvent>(e =>
        {
            HandleDocumentContentChanged(lsp, e.FilePath, e.Content, e.Version, e.FlushImmediately);
        }));

        _subscriptions.Add(host.Events.SubscribeSync<DocumentClosedEvent>(e =>
        {
            UnregisterDocument(e.FilePath);
            problems.ClearFile(ProblemSourceIds.Lsp, e.FilePath);
            if (!lsp.IsRunning) return;
            _scheduler.Schedule(
                $"lsp.close.{e.FilePath}",
                TaskPriority.Interactive,
                ct => FlushPendingDidChangeAndCloseAsync(lsp, e.FilePath, ct));
        }));

        _subscriptions.Add(host.Events.SubscribeSync<LspServerReadyEvent>(_ =>
        {
            foreach (var path in SnapshotDocumentPaths())
                _scheduler.Schedule(
                    $"lsp.ensure-open.{path}",
                    TaskPriority.Interactive,
                    ct => EnsureDocumentOpenAndQueueSemanticTokensAsync(lsp, path, ct),
                    correlationId: path);
        }));

        // Diagnostics signal OmniSharp finished analyzing — safe moment to fetch semantic tokens
        _subscriptions.Add(host.Events.SubscribeSync<DiagnosticsUpdatedEvent>(e =>
        {
            var diagnostics = e.Diagnostics;
            if (!IsOpenTextDocument(e.FilePath))
                return;

            _scheduler.ScheduleLatest(
                $"lsp.problems.{e.FilePath}",
                TaskPriority.Background,
                TimeSpan.Zero,
                ct =>
                {
                    if (ct.IsCancellationRequested)
                        return Task.CompletedTask;

                    problems.ReplaceFile(
                        ProblemSourceIds.Lsp,
                        e.FilePath,
                        diagnostics.Select(diagnostic => new ProblemItem(
                            FilePath: e.FilePath,
                            Line: diagnostic.StartLine,
                            Character: diagnostic.StartCharacter,
                            Severity: ToProblemSeverity(diagnostic.Severity),
                            Source: ProblemSourceIds.Lsp,
                            Code: diagnostic.Code,
                            Message: diagnostic.Message)).ToArray());

                    QueueDiagnosticsOutput(output);
                    return Task.CompletedTask;
                },
                correlationId: e.FilePath);
            if (!lsp.IsRunning) return;
            QueueSemanticTokens(e.FilePath);
        }));

        _subscriptions.Add(host.Events.SubscribeSync<FlushDocumentSyncEvent>(e =>
        {
            if (!lsp.IsRunning) return;
            _scheduler.Schedule(
                $"lsp.flush.{e.FilePath}",
                TaskPriority.Critical,
                ct => FlushPendingDidChangeImmediatelyAsync(lsp, e.FilePath, ct));
        }));

        _subscriptions.Add(host.Events.SubscribeSync<LspInteractiveRequestStartedEvent>(e =>
        {
            CancelSemanticTokens(e.FilePath);
        }));

        _subscriptions.Add(host.Events.SubscribeSync<GoToDefinitionRequestedEvent>(e =>
            _scheduler.Schedule("lsp.navigation", TaskPriority.Interactive,
                ct => HandleNavigation(host, nav, "definition", e.FilePath, e.Line, e.Character, ct),
                correlationId: $"{e.FilePath}:{e.Line}:{e.Character}")));

        _subscriptions.Add(host.Events.SubscribeSync<GoToImplementationRequestedEvent>(e =>
            _scheduler.Schedule("lsp.navigation", TaskPriority.Interactive,
                ct => HandleNavigation(host, nav, "implementation", e.FilePath, e.Line, e.Character, ct),
                correlationId: $"{e.FilePath}:{e.Line}:{e.Character}")));

        _subscriptions.Add(host.Events.SubscribeSync<GoToTypeDefinitionRequestedEvent>(e =>
            _scheduler.Schedule("lsp.navigation", TaskPriority.Interactive,
                ct => HandleNavigation(host, nav, "typeDefinition", e.FilePath, e.Line, e.Character, ct),
                correlationId: $"{e.FilePath}:{e.Line}:{e.Character}")));

        _subscriptions.Add(host.Events.SubscribeSync<LspProvisioningCompletedEvent>(_ =>
        {
            _provisioningPending = false;
            TryStartOrRestart(host, lsp, provisioning, output, fromProvisioning: true);
        }));

        host.SetModuleState(Name, ModuleState.Active);
    }

    private static ProblemSeverity ToProblemSeverity(LspDiagnosticSeverity severity) => severity switch
    {
        LspDiagnosticSeverity.Error => ProblemSeverity.Error,
        LspDiagnosticSeverity.Warning => ProblemSeverity.Warning,
        LspDiagnosticSeverity.Information => ProblemSeverity.Information,
        LspDiagnosticSeverity.Hint => ProblemSeverity.Hint,
        _ => ProblemSeverity.Information,
    };

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

    private void TryStartOrRestart(
        IModuleHost host,
        ILanguageServerService lsp,
        ILspProvisioningService provisioning,
        IOutputChannelService output,
        bool fromProvisioning)
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
                _ = output.WriteAsync(OutputChannelIds.Output, "[LanguageServer] OmniSharp provisioning required\r\n");
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

        _scheduler?.CancelAndForget($"lsp.ensure-open.{filePath}");
        _scheduler?.CancelAndForget($"lsp.didchange.{filePath}");
        _scheduler?.CancelAndForget($"lsp.problems.{filePath}");
        _scheduler?.CancelAndForget($"lsp.semantic.{filePath}");
        _scheduler?.CancelAndForget($"lsp.flush.{filePath}");

        _eventBus?.Publish(new SemanticTokensRefreshFinishedEvent(filePath));
    }

    private bool IsOpenTextDocument(string filePath)
    {
        lock (_documentGate)
            return _documents.ContainsKey(filePath);
    }

    private void PublishCachedDiagnosticsForOpenDocument(
        IDiagnosticsService diagnosticsService,
        IProblemService problems,
        IOutputChannelService output,
        string filePath)
    {
        if (!IsOpenTextDocument(filePath))
            return;

        var diagnostics = diagnosticsService.GetDiagnostics(filePath);
        if (diagnostics.Count == 0)
            return;

        problems.ReplaceFile(
            ProblemSourceIds.Lsp,
            filePath,
            diagnostics.Select(diagnostic => new ProblemItem(
                FilePath: filePath,
                Line: diagnostic.StartLine,
                Character: diagnostic.StartCharacter,
                Severity: ToProblemSeverity(diagnostic.Severity),
                Source: ProblemSourceIds.Lsp,
                Code: diagnostic.Code,
                Message: diagnostic.Message)).ToArray());

        QueueDiagnosticsOutput(output);
    }

    private void QueueDiagnosticsOutput(IOutputChannelService output)
    {
        Interlocked.Increment(ref _diagnosticsPublishedOpenFileCount);
        _scheduler!.ScheduleLatest(
            "lsp.diagnostics-output",
            TaskPriority.Background,
            TimeSpan.FromMilliseconds(750),
            _ =>
            {
                var count = Interlocked.Exchange(ref _diagnosticsPublishedOpenFileCount, 0);
                if (count <= 0)
                    return Task.CompletedTask;

                var suffix = count == 1 ? "file" : "files";
                return output.WriteAsync(
                    OutputChannelIds.Output,
                    $"[LanguageServer] Diagnostics: {count} open {suffix} updated\r\n");
            });
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
