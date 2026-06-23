using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Models.Output;
using Fluence.Modules.LanguageServer;
using Fluence.Infrastructure.Protocols.Lsp;
using Fluence.Core.Events.Provisioning;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed partial class LanguageServerService : ILanguageServerService, IAsyncDisposable
{
    private readonly ILspProvisioningService _provisioning;
    private readonly ILspArgumentsBuilder _argumentsBuilder;
    private readonly IProcessSpawner _processSpawner;
    private readonly IDiagnosticsService _diagnostics;
    private readonly IShellEventBus _events;
    private readonly IOutputChannelService _output;
    private readonly LspClientHolder _holder;
    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LspClient? _client;
    private string _serverDisplayName = "LanguageServer";

    public bool IsRunning => _client is not null;

    public IReadOnlyList<string> SemanticTokenTypes { get; private set; } = [];
    public IReadOnlyList<string> SemanticTokenModifiers { get; private set; } = [];

    public LanguageServerService(
        ILspProvisioningService provisioning,
        ILspArgumentsBuilder argumentsBuilder,
        IProcessSpawner processSpawner,
        IDiagnosticsService diagnostics,
        IShellEventBus events,
        IOutputChannelService output,
        ISettingsService settings,
        LspClientHolder holder)
    {
        _provisioning = provisioning;
        _argumentsBuilder = argumentsBuilder;
        _processSpawner = processSpawner;
        _diagnostics = diagnostics;
        _events = events;
        _output = output;
        _holder = holder;
        _settings = settings;
    }

    public async Task StartAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is not null)
                await StopCoreAsync().ConfigureAwait(false);

            var runtimeSettings = LanguageServerRuntimeSettings.From(_settings.Get<LanguageServerSettings>());
            await StartCoreAsync(rootPath, runtimeSettings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Language server shutdown failed: {ex}");
        }
    }

    private async Task StartCoreAsync(
        string rootPath,
        LanguageServerRuntimeSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var executable = _provisioning.GetExecutablePath();
            _serverDisplayName = GetServerDisplayName(executable);
            var arguments = _argumentsBuilder.Build(rootPath, _provisioning, _settings);
            var env = _provisioning.GetLaunchEnvironment();
            WriteStartupOutput(executable, rootPath, env);

            _client = new LspClient(_processSpawner);
            _holder.Client = _client;
            _client.NotificationReceived += OnNotificationReceived;
            _client.Disconnected += OnClientDisconnected;
            _client.StderrLineReceived += OnStderrLine;

            await _client.StartAsync(executable, arguments, null, env, cancellationToken).ConfigureAwait(false);

            // LSP handshake
            var initResult = await _client.SendRequestAsync("initialize", BuildInitializeParams(rootPath), cancellationToken)
                .ConfigureAwait(false);

            CaptureSemanticTokenLegend(initResult);

            await _client.SendNotificationAsync("initialized", new System.Text.Json.Nodes.JsonObject(), cancellationToken)
                .ConfigureAwait(false);
            await _client.SendNotificationAsync("workspace/didChangeConfiguration", BuildConfigurationParams(settings), cancellationToken)
                .ConfigureAwait(false);

            WriteOutput($"[LanguageServer] {_serverDisplayName} ready\r\n");
            _events.Publish(new LspServerReadyEvent());
        }
        catch
        {
            await StopCoreAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task StopCoreAsync()
    {
        if (_client is null)
            return;

        WriteOutput($"[LanguageServer] Stopping {_serverDisplayName}\r\n");
        var client = _client;
        _client = null;
        _holder.Client = null;
        client.NotificationReceived -= OnNotificationReceived;
        client.Disconnected -= OnClientDisconnected;
        client.StderrLineReceived -= OnStderrLine;

        try
        {
            await client.SendNotificationAsync("exit", null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[LS] exit notification failed: {ex.Message}"); }

        await client.DisposeAsync().ConfigureAwait(false);
        WriteOutput($"[LanguageServer] {_serverDisplayName} stopped\r\n");
    }

    private void OnStderrLine(string line) =>
        WriteOutput($"[{_serverDisplayName}] {line}\r\n");

    private void WriteOutput(string text, OutputLogLevel kind = OutputLogLevel.Information) =>
        _ = _output.WriteAsync(Entrypoint.ChannelId, text, kind);

    private void WriteStartupOutput(
        string executable,
        string rootPath,
        IReadOnlyDictionary<string, string> environment)
    {
        WriteOutput($"[LanguageServer] Starting for {rootPath}\r\n");
        WriteOutput($"[LanguageServer] Executable: {executable}\r\n");

        var version = TryGetExecutableVersion(executable);
        if (!string.IsNullOrWhiteSpace(version))
            WriteOutput($"[LanguageServer] Version: {version}\r\n");

        foreach (var (key, value) in environment)
            WriteOutput($"[LanguageServer] {key}: {value}\r\n");

        WriteOutput($"[LanguageServer] Root: {rootPath}\r\n");
    }

    private static string? TryGetExecutableVersion(string executable)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executable);
            return string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion;
        }
        catch
        {
            return null;
        }
    }

    private static string GetServerDisplayName(string executable)
    {
        var fileName = Path.GetFileNameWithoutExtension(executable);
        return string.IsNullOrWhiteSpace(fileName) ? "LanguageServer" : fileName;
    }

    private sealed record LanguageServerRuntimeSettings(
        bool EnableMsBuild,
        bool LoadProjectsOnDemand,
        bool EnablePackageAutoRestore,
        bool EnableAnalyzersSupport,
        bool EnableDecompilationSupport,
        bool EnableImportCompletion,
        int DiagnosticWorkersThreadCount,
        bool EnableEditorConfigSupport,
        bool IncludePrereleases)
    {
        public static LanguageServerRuntimeSettings From(LanguageServerSettings settings) => new(
            settings.EnableMsBuild,
            settings.LoadProjectsOnDemand,
            settings.EnablePackageAutoRestore,
            settings.EnableAnalyzersSupport,
            settings.EnableDecompilationSupport,
            settings.EnableImportCompletion,
            Math.Max(1, settings.DiagnosticWorkersThreadCount),
            settings.EnableEditorConfigSupport,
            settings.IncludePrereleases);
    }
}
