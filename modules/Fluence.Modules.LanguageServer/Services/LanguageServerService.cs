using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Infrastructure.Protocols.Lsp;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed partial class LanguageServerService : ILanguageServerService, IAsyncDisposable
{
    private readonly ILspProvisioningService _provisioning;
    private readonly IProcessSpawner _processSpawner;
    private readonly IDiagnosticsService _diagnostics;
    private readonly IShellEventBus _events;
    private readonly LspClientHolder _holder;
    private LspClient? _client;

    public bool IsRunning => _client is not null;

    public IReadOnlyList<string> SemanticTokenTypes { get; private set; } = [];
    public IReadOnlyList<string> SemanticTokenModifiers { get; private set; } = [];

    public LanguageServerService(
        ILspProvisioningService provisioning,
        IProcessSpawner processSpawner,
        IDiagnosticsService diagnostics,
        IShellEventBus events,
        LspClientHolder holder)
    {
        _provisioning = provisioning;
        _processSpawner = processSpawner;
        _diagnostics = diagnostics;
        _events = events;
        _holder = holder;
    }

    public async Task StartAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        if (_client is not null)
            await StopAsync().ConfigureAwait(false);

        var executable = _provisioning.GetExecutablePath();
        var arguments = $"--languageserver -z -s \"{rootPath}\"";

        _client = new LspClient(_processSpawner);
        _holder.Client = _client;
        _client.NotificationReceived += OnNotificationReceived;
        _client.Disconnected += OnClientDisconnected;

        var env = _provisioning.GetLaunchEnvironment();
        await _client.StartAsync(executable, arguments, null, env, cancellationToken).ConfigureAwait(false);

        // LSP handshake
        var initResult = await _client.SendRequestAsync("initialize", BuildInitializeParams(rootPath), cancellationToken)
            .ConfigureAwait(false);

        CaptureSemanticTokenLegend(initResult);

        await _client.SendNotificationAsync("initialized", new System.Text.Json.Nodes.JsonObject(), cancellationToken)
            .ConfigureAwait(false);

        _events.Publish(new LspServerReadyEvent());
    }

    public async Task StopAsync()
    {
        if (_client is null)
            return;

        var client = _client;
        _client = null;
        _holder.Client = null;
        client.NotificationReceived -= OnNotificationReceived;
        client.Disconnected -= OnClientDisconnected;

        try
        {
            await client.SendNotificationAsync("exit", null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[LS] exit notification failed: {ex.Message}"); }

        await client.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
