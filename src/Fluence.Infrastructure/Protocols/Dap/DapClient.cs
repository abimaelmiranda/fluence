using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Services.Debugging;

namespace Fluence.Infrastructure.Protocols.Dap;

public sealed class DapClient : IDebugAdapterClient
{
    private readonly string _adapterPath;
    private readonly string _adapterId;
    private readonly IProcessSpawner _spawner;
    private readonly string _logPath;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject?>> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly TaskCompletionSource _initializedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private IReadOnlySet<string> _exceptionBreakpointFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private ITrackedProcess? _trackedProcess;
    private Process? _process;
    private StreamWriter? _stdin;
    private Stream? _stdout;
    private int _seq;
    private int _threadId;
    private Task? _readLoopTask;
    private Task? _errorLoopTask;
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LoopDrainTimeout = TimeSpan.FromMilliseconds(500);

    public DapClient(string adapterPath, string adapterId, string logPath, IProcessSpawner spawner)
    {
        _adapterPath = adapterPath;
        _adapterId = adapterId;
        _logPath = logPath;
        _spawner = spawner;
    }

    public event EventHandler<DebugAdapterStoppedEvent>? Stopped;

    public event EventHandler<DebugAdapterTerminatedEvent>? Terminated;

    public event EventHandler<DebugAdapterContinuedEvent>? Continued;

    public event EventHandler<DebugAdapterOutputEvent>? OutputReceived;

    public async Task StartAsync(DebugLaunchRequest request, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);

        var startInfo = new ProcessStartInfo
            {
                FileName = _adapterPath,
                ArgumentList = { "--interpreter=vscode" },
                WorkingDirectory = request.WorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        _trackedProcess = await _spawner.StartAsync(startInfo, "DebugAdapter", cancellationToken).ConfigureAwait(false);
        _process = _trackedProcess.Process;
        _process.Exited += (_, _) =>
        {
            var code = _process?.ExitCode;
            _ = LogAsync($"[{_adapterId}] Process exited, code={code}", CancellationToken.None);
            if (code is not 0)
                OutputReceived?.Invoke(this, new DebugAdapterOutputEvent($"[{_adapterId}] Process exited with code {code}{Environment.NewLine}", true));
            Terminated?.Invoke(this, new DebugAdapterTerminatedEvent());
        };

        OutputReceived?.Invoke(this, new DebugAdapterOutputEvent($"[debug] {_adapterId}: {_adapterPath}{Environment.NewLine}", false));

        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput.BaseStream;
        _readLoopTask = Task.Run(async () =>
        {
            try { await ReadLoopAsync(_disposeCts.Token).ConfigureAwait(false); }
            catch (Exception) { }
            finally
            {
                // When the read loop exits for any reason (EOF, error, or dispose), unblock
                // all pending requests immediately instead of waiting for the 3-second timeout.
                CancelAllPendingRequests();
            }
        });
        _errorLoopTask = Task.Run(async () =>
        {
            try { await ReadErrorLoopAsync(_process, _disposeCts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        });

        var initializeResponse = await SendRequestAsync("initialize", new JsonObject
        {
            ["adapterID"] = _adapterId,
            ["clientID"] = "fluence",
            ["clientName"] = "Fluence IDE",
            ["linesStartAt1"] = true,
            ["columnsStartAt1"] = true,
            ["pathFormat"] = "path",
        }, cancellationToken).ConfigureAwait(false);
        _exceptionBreakpointFilters = ParseExceptionBreakpointFilters(initializeResponse);

        var initializedWait = Stopwatch.StartNew();
        await SendRequestAsync("launch", request.LaunchArguments, cancellationToken).ConfigureAwait(false);

        // Wait for the adapter's "initialized" event before returning.
        // Sending configuration before this event arrives violates the DAP contract and
        // can cause the adapter to terminate the session prematurely.
        using var initTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        initTimeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            await _initializedTcs.Task.WaitAsync(initTimeout.Token).ConfigureAwait(false);
            await LogAsync($"[{_adapterId}] initialized event after {initializedWait.ElapsedMilliseconds}ms", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await LogAsync($"[{_adapterId}] initialized event timeout after {initializedWait.ElapsedMilliseconds}ms", CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    public async Task CompleteConfigurationAsync(CancellationToken cancellationToken = default)
    {
        await SendRequestAsync("configurationDone", new JsonObject(), cancellationToken).ConfigureAwait(false);
    }

    public async Task SetExceptionBreakpointsAsync(
        DebugExceptionBreakMode mode,
        CancellationToken cancellationToken = default)
    {
        var filters = ResolveExceptionFilters(mode);
        if (filters.Length == 0)
        {
            OutputReceived?.Invoke(this, new DebugAdapterOutputEvent(
                "[debug] Exception breakpoints are not supported by this debug adapter.\r\n",
                false));
            return;
        }

        await SendRequestAsync("setExceptionBreakpoints", new JsonObject
        {
            ["filters"] = new JsonArray(filters.Select(filter => JsonValue.Create(filter)).ToArray()),
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<DebugBreakpoint>>> SetBreakpointsAsync(
        IReadOnlyList<DebugBreakpoint> breakpoints,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, IReadOnlyList<DebugBreakpoint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in breakpoints.Where(b => b.IsEnabled).GroupBy(b => b.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            var response = await SendRequestAsync("setBreakpoints", new JsonObject
            {
                ["source"] = new JsonObject { ["path"] = group.Key },
                ["breakpoints"] = new JsonArray(group.Select(b => new JsonObject { ["line"] = b.Line }).ToArray<JsonNode?>()),
                ["sourceModified"] = false,
            }, cancellationToken).ConfigureAwait(false);

            var responseBreakpoints = response?["breakpoints"]?.AsArray();
            if (responseBreakpoints is null)
                continue;

            result[group.Key] = responseBreakpoints
                .OfType<JsonObject>()
                .Select((breakpoint, index) =>
                {
                    var requested = group.ElementAtOrDefault(index);
                    return new DebugBreakpoint(
                        FilePath: group.Key,
                        Line: breakpoint["line"]?.GetValue<int>() ?? requested?.Line ?? 0,
                        IsEnabled: requested?.IsEnabled ?? true,
                        IsVerified: breakpoint["verified"]?.GetValue<bool>() ?? false,
                        AdapterId: breakpoint["id"]?.GetValue<int>(),
                        Message: breakpoint["message"]?.GetValue<string>());
                })
                .Where(b => b.Line > 0)
                .ToArray();
        }

        return result;
    }

    public async Task<IReadOnlyList<DebugBreakpoint>> SetBreakpointsForFileAsync(
        string filePath,
        IReadOnlyList<DebugBreakpoint> breakpoints,
        CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("setBreakpoints", new JsonObject
        {
            ["source"] = new JsonObject { ["path"] = filePath },
            ["breakpoints"] = new JsonArray(breakpoints.Where(b => b.IsEnabled).Select(b => new JsonObject { ["line"] = b.Line }).ToArray<JsonNode?>()),
            ["sourceModified"] = false,
        }, cancellationToken).ConfigureAwait(false);

        var responseBreakpoints = response?["breakpoints"]?.AsArray();
        if (responseBreakpoints is null)
            return [];

        return responseBreakpoints
            .OfType<JsonObject>()
            .Select((breakpoint, index) =>
            {
                var requested = breakpoints.ElementAtOrDefault(index);
                return new DebugBreakpoint(
                    FilePath: filePath,
                    Line: breakpoint["line"]?.GetValue<int>() ?? requested?.Line ?? 0,
                    IsEnabled: requested?.IsEnabled ?? true,
                    IsVerified: breakpoint["verified"]?.GetValue<bool>() ?? false,
                    AdapterId: breakpoint["id"]?.GetValue<int>(),
                    Message: breakpoint["message"]?.GetValue<string>());
            })
            .Where(b => b.Line > 0)
            .ToArray();
    }

    public async Task<IReadOnlyList<DebugStackFrame>> GetStackTraceAsync(int threadId, CancellationToken cancellationToken = default)
    {
        _threadId = threadId;
        var response = await SendRequestAsync("stackTrace", new JsonObject
        {
            ["threadId"] = threadId,
            ["startFrame"] = 0,
            ["levels"] = 20,
        }, cancellationToken).ConfigureAwait(false);

        var frames = response?["stackFrames"]?.AsArray();
        if (frames is null)
            return [];

        return frames
            .OfType<JsonObject>()
            .Select(frame => new DebugStackFrame(
                Id: frame["id"]?.GetValue<int>() ?? 0,
                Name: frame["name"]?.GetValue<string>() ?? "(unknown)",
                FilePath: frame["source"]?["path"]?.GetValue<string>(),
                Line: frame["line"]?.GetValue<int>() ?? 0))
            .ToArray();
    }

    public async Task<IReadOnlyList<DebugVariable>> GetVariablesAsync(int frameId, CancellationToken cancellationToken = default)
    {
        var scopesResponse = await SendRequestAsync("scopes", new JsonObject
        {
            ["frameId"] = frameId,
        }, cancellationToken).ConfigureAwait(false);

        var scopes = scopesResponse?["scopes"]?.AsArray();
        if (scopes is null)
            return [];

        var variables = new List<DebugVariable>();
        foreach (var scope in scopes.OfType<JsonObject>())
        {
            var reference = scope["variablesReference"]?.GetValue<int>() ?? 0;
            if (reference <= 0)
                continue;

            var variablesResponse = await SendRequestAsync("variables", new JsonObject
            {
                ["variablesReference"] = reference,
            }, cancellationToken).ConfigureAwait(false);
            var values = variablesResponse?["variables"]?.AsArray();
            if (values is null)
                continue;

            variables.AddRange(values.OfType<JsonObject>().Select(ParseVariable));
        }

        return variables;
    }

    public async Task<IReadOnlyList<DebugVariable>> GetChildVariablesAsync(int variablesReference, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("variables", new JsonObject
        {
            ["variablesReference"] = variablesReference,
        }, cancellationToken).ConfigureAwait(false);

        var values = response?["variables"]?.AsArray();
        if (values is null)
            return [];

        return values.OfType<JsonObject>().Select(ParseVariable).ToArray();
    }

    private static DebugVariable ParseVariable(JsonObject variable) => new(
        Name: variable["name"]?.GetValue<string>() ?? string.Empty,
        Value: variable["value"]?.GetValue<string>() ?? string.Empty,
        Type: variable["type"]?.GetValue<string>() ?? string.Empty,
        VariablesReference: variable["variablesReference"]?.GetValue<int>() ?? 0);

    public async Task<DebugVariable?> EvaluateAsync(string expression, int frameId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await SendRequestAsync("evaluate", new JsonObject
            {
                ["expression"] = expression,
                ["frameId"] = frameId,
                ["context"] = "hover",
            }, cancellationToken).ConfigureAwait(false);

            var result = response?["result"]?.GetValue<string>();
            if (result is null)
                return null;

            return new DebugVariable(
                Name: expression,
                Value: result,
                Type: response?["type"]?.GetValue<string>() ?? string.Empty,
                VariablesReference: response?["variablesReference"]?.GetValue<int>() ?? 0);
        }
        catch
        {
            return null;
        }
    }

    public Task ContinueAsync(CancellationToken cancellationToken = default) =>
        SendControlRequestAsync("continue", cancellationToken);

    public Task StepOverAsync(CancellationToken cancellationToken = default) =>
        SendControlRequestAsync("next", cancellationToken);

    public Task StepIntoAsync(CancellationToken cancellationToken = default) =>
        SendControlRequestAsync("stepIn", cancellationToken);

    public Task StepOutAsync(CancellationToken cancellationToken = default) =>
        SendControlRequestAsync("stepOut", cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(750));
        try { await SendRequestAsync("terminate", new JsonObject(), timeout.Token).ConfigureAwait(false); }
        catch { }
        try { await SendRequestAsync("disconnect", new JsonObject { ["terminateDebuggee"] = true }, timeout.Token).ConfigureAwait(false); }
        catch { }
        KillProcess();
    }

    private void CancelAllPendingRequests()
    {
        var keys = _pending.Keys.ToArray();
        foreach (var key in keys)
        {
            if (_pending.TryRemove(key, out var tcs))
                tcs.TrySetCanceled();
        }
    }

    private async Task SendControlRequestAsync(string command, CancellationToken cancellationToken)
    {
        await SendRequestAsync(command, new JsonObject { ["threadId"] = _threadId }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonObject?> SendRequestAsync(
        string command,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var seq = Interlocked.Increment(ref _seq);
        var tcs = new TaskCompletionSource<JsonObject?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[seq] = tcs;

        var request = new JsonObject
        {
            ["seq"] = seq,
            ["type"] = "request",
            ["command"] = command,
            ["arguments"] = arguments,
        };

        try
        {
            await WriteMessageAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(seq, out var orphaned);
            orphaned?.TrySetCanceled();
            throw;
        }
        timeout.CancelAfter(RequestTimeout);
        using var timeoutReg = timeout.Token.Register(() =>
        {
            if (_pending.TryRemove(seq, out var pending))
                pending.TrySetCanceled(timeout.Token);
        });
        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task WriteMessageAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var json = message.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {bytes.Length}\r\n\r\n";

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LogAsync(">> " + json, cancellationToken).ConfigureAwait(false);
            await _stdin!.WriteAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _stdin.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _stdout is not null)
        {
            var length = await ReadContentLengthAsync(_stdout, cancellationToken).ConfigureAwait(false);
            if (length <= 0)
                return;

            var buffer = new byte[length];
            await _stdout.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            var json = Encoding.UTF8.GetString(buffer);
            await LogAsync("<< " + json, cancellationToken).ConfigureAwait(false);
            HandleMessage(JsonNode.Parse(json)?.AsObject());
        }
    }

    private static async Task<int> ReadContentLengthAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new StringBuilder();
        var buffer = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return 0;

            header.Append((char)buffer[0]);
            if (header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                break;
        }

        foreach (var line in header.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2 &&
                string.Equals(parts[0], "Content-Length", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(parts[1].Trim(), out var length))
            {
                return length;
            }
        }

        return 0;
    }

    private void HandleMessage(JsonObject? message)
    {
        if (message is null)
            return;

        var type = message["type"]?.GetValue<string>();
        if (type == "response")
        {
            var requestSeq = message["request_seq"]?.GetValue<int>() ?? 0;
            if (_pending.TryRemove(requestSeq, out var pending))
            {
                var success = message["success"]?.GetValue<bool>() ?? false;
                if (success)
                {
                    pending.TrySetResult(message["body"]?.AsObject());
                }
                else
                {
                    var command = message["command"]?.GetValue<string>() ?? "unknown";
                    var error = message["message"]?.GetValue<string>() ?? "DAP request failed.";
                    pending.TrySetException(new InvalidOperationException($"DAP command '{command}' failed: {error}"));
                }
            }
            return;
        }

        if (type != "event")
            return;

        var eventName = message["event"]?.GetValue<string>();
        var body = message["body"]?.AsObject();
        switch (eventName)
        {
            case "initialized":
                _initializedTcs.TrySetResult();
                break;
            case "stopped":
                _threadId = body?["threadId"]?.GetValue<int>() ?? _threadId;
                Stopped?.Invoke(this, new DebugAdapterStoppedEvent(
                    body?["reason"]?.GetValue<string>(),
                    _threadId,
                    body?["description"]?.GetValue<string>(),
                    body?["text"]?.GetValue<string>()));
                break;
            case "terminated":
            case "exited":
                Terminated?.Invoke(this, new DebugAdapterTerminatedEvent());
                break;
            case "continued":
                Continued?.Invoke(this, new DebugAdapterContinuedEvent());
                break;
            case "output":
                OutputReceived?.Invoke(this, new DebugAdapterOutputEvent(
                    body?["output"]?.GetValue<string>() ?? string.Empty,
                    string.Equals(body?["category"]?.GetValue<string>(), "stderr", StringComparison.OrdinalIgnoreCase)));
                break;
        }
    }

    private async Task ReadErrorLoopAsync(Process process, CancellationToken cancellationToken)
    {
        string? line;
        var streamReader = process.StandardError;
        while (!cancellationToken.IsCancellationRequested
            && (line = await streamReader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            await LogAsync("[stderr] " + line, CancellationToken.None).ConfigureAwait(false);
            OutputReceived?.Invoke(this, new DebugAdapterOutputEvent($"[{_adapterId}] " + line + Environment.NewLine, true));
        }
    }

    private async Task LogAsync(string line, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        await File.AppendAllTextAsync(_logPath, line + Environment.NewLine).ConfigureAwait(false);
    }

    private static JsonObject CreateEnvironmentObject(IReadOnlyDictionary<string, string> values)
    {
        var obj = new JsonObject();

        // Forward the user's explicit environment first; these take precedence.
        foreach (var pair in values)
            obj[pair.Key] = pair.Value;

        // The debuggee inherits the adapter environment.
        // Inject an augmented PATH and DOTNET_ROOT unless the user overrode them.
        if (!obj.ContainsKey("PATH"))
        {
            var augmented = ProcessEnvironment.GetAugmentedPath();
            if (!string.IsNullOrWhiteSpace(augmented))
                obj["PATH"] = augmented;
        }

        if (!obj.ContainsKey("DOTNET_ROOT"))
        {
            var dotnetRoot = ProcessEnvironment.ResolveDotnetRoot();
            if (dotnetRoot is not null)
                obj["DOTNET_ROOT"] = dotnetRoot;
        }

        return obj;
    }

    private string[] ResolveExceptionFilters(DebugExceptionBreakMode mode)
    {
        var candidates = mode == DebugExceptionBreakMode.StopInAllExceptions
            ? new[] { "all" }
            : new[] { "user-unhandled", "userUnhandled", "unhandled", "uncaught" };

        var available = _exceptionBreakpointFilters;
        if (available.Count == 0)
            return [];

        return candidates
            .Where(available.Contains)
            .Take(1)
            .ToArray();
    }

    private static IReadOnlySet<string> ParseExceptionBreakpointFilters(JsonObject? initializeResponse)
    {
        var filters = initializeResponse?["exceptionBreakpointFilters"]?.AsArray();
        if (filters is null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return filters
            .OfType<JsonObject>()
            .Select(filter => filter["filter"]?.GetValue<string>())
            .Where(filter => !string.IsNullOrWhiteSpace(filter))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void KillProcess()
    {
        try
        {
            if (_process is { HasExited: false })
                _trackedProcess?.KillTree();
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        KillProcess();
        if (_process is not null)
        {
            try
            {
                await _process.WaitForExitAsync()
                    .WaitAsync(DisposeTimeout)
                    .ConfigureAwait(false);
            }
            catch
            {
            }
        }
        try
        {
            var loopDrain = Task.WhenAll(
                _readLoopTask ?? Task.CompletedTask,
                _errorLoopTask ?? Task.CompletedTask);
            await loopDrain.WaitAsync(LoopDrainTimeout).ConfigureAwait(false);
        }
        catch { }
        if (_trackedProcess is not null)
            await _trackedProcess.DisposeAsync().ConfigureAwait(false);
        _trackedProcess = null;
        _process = null;
        _disposeCts.Dispose();
        _writeGate.Dispose();
    }
}
