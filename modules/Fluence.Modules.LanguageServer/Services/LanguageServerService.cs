using System;
using System.Collections.Generic;
using System.Diagnostics;
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

namespace Fluence.Modules.LanguageServer.Services;

internal sealed partial class LanguageServerService : ILanguageServerService, IAsyncDisposable
{
    private readonly ILspProvisioningService _provisioning;
    private readonly IProcessSpawner _processSpawner;
    private readonly IDiagnosticsService _diagnostics;
    private readonly IShellEventBus _events;
    private readonly IOutputChannelService _output;
    private readonly LspClientHolder _holder;
    private readonly ISettingsService _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
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
        ISettingsService settings,
        LspClientHolder holder)
    {
        _provisioning = provisioning;
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
            var sdkPath = _provisioning.GetSelectedSdkPath();
            var arguments = BuildOmniSharpArguments(rootPath, sdkPath, settings);
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
            await _client.SendNotificationAsync("workspace/didChangeConfiguration", BuildConfigurationParams(settings), cancellationToken)
                .ConfigureAwait(false);

            WriteOutput("[LanguageServer] OmniSharp ready\r\n");
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

    private static string BuildOmniSharpArguments(
        string rootPath,
        string? sdkPath,
        LanguageServerRuntimeSettings settings)
    {
        var arguments = new List<string>
        {
            "--languageserver",
            "-z",
            "-s",
            QuoteArgument(rootPath),
            $"--msbuild:enabled={Bool(settings.EnableMsBuild)}",
            $"--msbuild:loadProjectsOnDemand={Bool(settings.LoadProjectsOnDemand)}",
            $"--msbuild:EnablePackageAutoRestore={Bool(settings.EnablePackageAutoRestore)}",
            $"--RoslynExtensionsOptions:enableAnalyzersSupport={Bool(settings.EnableAnalyzersSupport)}",
            $"--RoslynExtensionsOptions:enableDecompilationSupport={Bool(settings.EnableDecompilationSupport)}",
            $"--RoslynExtensionsOptions:enableImportCompletion={Bool(settings.EnableImportCompletion)}",
            $"--RoslynExtensionsOptions:diagnosticWorkersThreadCount={settings.DiagnosticWorkersThreadCount}",
            $"--FormattingOptions:enableEditorConfigSupport={Bool(settings.EnableEditorConfigSupport)}",
            $"--sdk:includePrereleases={Bool(settings.IncludePrereleases)}",
        };

        if (!string.IsNullOrWhiteSpace(sdkPath))
            arguments.Add($"--sdk:path={QuoteArgument(sdkPath)}");

        return string.Join(' ', arguments);
    }

    private static string Bool(bool value) => value ? "true" : "false";

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
