using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Models.Output;
using Fluence.Infrastructure.Protocols.Lsp;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed partial class LanguageServerService : ILanguageServerService, IAsyncDisposable
{
    private readonly ILspProvisioningService _provisioning;
    private readonly IProcessSpawner _processSpawner;
    private readonly IDiagnosticsService _diagnostics;
    private readonly IShellEventBus _events;
    private readonly IOutputChannelService _output;
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
        IOutputChannelService output,
        LspClientHolder holder)
    {
        _provisioning = provisioning;
        _processSpawner = processSpawner;
        _diagnostics = diagnostics;
        _events = events;
        _output = output;
        _holder = holder;
    }

    public async Task StartAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        if (_client is not null)
            await StopAsync().ConfigureAwait(false);

        var executable = _provisioning.GetExecutablePath();
        var sdkPath = _provisioning.GetSelectedSdkPath();
        var arguments = BuildOmniSharpArguments(rootPath, sdkPath);
        var env = _provisioning.GetLaunchEnvironment();
        WriteStartupOutput(executable, rootPath, env, sdkPath);

        _client = new LspClient(_processSpawner);
        _holder.Client = _client;
        _client.NotificationReceived += OnNotificationReceived;
        _client.Disconnected += OnClientDisconnected;

        await _client.StartAsync(executable, arguments, null, env, cancellationToken).ConfigureAwait(false);

        // LSP handshake
        var initResult = await _client.SendRequestAsync("initialize", BuildInitializeParams(rootPath), cancellationToken)
            .ConfigureAwait(false);

        CaptureSemanticTokenLegend(initResult);

        await _client.SendNotificationAsync("initialized", new System.Text.Json.Nodes.JsonObject(), cancellationToken)
            .ConfigureAwait(false);
        await _client.SendNotificationAsync("workspace/didChangeConfiguration", BuildConfigurationParams(), cancellationToken)
            .ConfigureAwait(false);

        WriteOutput("[LanguageServer] OmniSharp ready\r\n");
        _events.Publish(new LspServerReadyEvent());
    }

    public async Task StopAsync()
    {
        if (_client is null)
            return;

        WriteOutput("[LanguageServer] Stopping OmniSharp\r\n");
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
        WriteOutput("[LanguageServer] OmniSharp stopped\r\n");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private void WriteOutput(string text, OutputChannelEntryKind kind = OutputChannelEntryKind.Information) =>
        _ = _output.WriteAsync(OutputChannelIds.Output, text, kind);

    private void WriteStartupOutput(
        string executable,
        string rootPath,
        IReadOnlyDictionary<string, string> environment,
        string? sdkPath)
    {
        WriteOutput($"[LanguageServer] Starting OmniSharp for {rootPath}\r\n");
        WriteOutput($"[LanguageServer] OmniSharp: {executable}\r\n");

        var version = TryGetExecutableVersion(executable);
        if (!string.IsNullOrWhiteSpace(version))
            WriteOutput($"[LanguageServer] OmniSharp version: {version}\r\n");

        if (environment.TryGetValue("DOTNET_ROOT", out var dotnetRoot))
            WriteOutput($"[LanguageServer] DOTNET_ROOT: {dotnetRoot}\r\n");

        if (environment.TryGetValue("DOTNET_HOST_PATH", out var dotnetHostPath))
            WriteOutput($"[LanguageServer] DOTNET_HOST_PATH: {dotnetHostPath}\r\n");

        WriteOutput($"[LanguageServer] SDK: {sdkPath ?? "(not detected)"}\r\n");
        WriteOutput($"[LanguageServer] Solution root: {rootPath}\r\n");
    }

    private static string BuildOmniSharpArguments(string rootPath, string? sdkPath)
    {
        // TODO: Load these OmniSharp defaults from Fluence settings when LSP customization is exposed.
        var arguments = new List<string>
        {
            "--languageserver",
            "-z",
            "-s",
            QuoteArgument(rootPath),
            "--msbuild:enabled=true",
            "--msbuild:loadProjectsOnDemand=false",
            "--msbuild:EnablePackageAutoRestore=true",
            "--RoslynExtensionsOptions:enableAnalyzersSupport=true",
            "--RoslynExtensionsOptions:enableDecompilationSupport=true",
            "--RoslynExtensionsOptions:enableImportCompletion=true",
            "--RoslynExtensionsOptions:diagnosticWorkersThreadCount=1",
            "--FormattingOptions:enableEditorConfigSupport=true",
            "--sdk:includePrereleases=true",
        };

        if (!string.IsNullOrWhiteSpace(sdkPath))
            arguments.Add($"--sdk:path={QuoteArgument(sdkPath)}");

        return string.Join(' ', arguments);
    }

    private static string QuoteArgument(string value) =>
        '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';

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
}
