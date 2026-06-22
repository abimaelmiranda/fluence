using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Infrastructure;

namespace Fluence.Infrastructure.Protocols.Lsp;

public sealed class LspClient : IAsyncDisposable
{
    private readonly IProcessSpawner _spawner;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private ITrackedProcess? _trackedProcess;
    private Process? _process;
    private StreamWriter? _stdin;
    private Stream? _stdout;
    private int _nextId;
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(2);

    public event Action<string, JsonNode?, int?>? NotificationReceived;
    public event Action? Disconnected;
    public event Action<string>? StderrLineReceived;

    public LspClient(IProcessSpawner spawner)
    {
        _spawner = spawner;
    }

    public async Task StartAsync(
        string executable,
        string arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
                psi.Environment[key] = value;
        }

        _trackedProcess = await _spawner.StartAsync(psi, "LanguageServer", cancellationToken).ConfigureAwait(false);
        _process = _trackedProcess.Process;

        _process.Exited += (_, _) =>
        {
            CancelAllPendingRequests();
            Disconnected?.Invoke();
        };

        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput.BaseStream;

        _ = Task.Run(async () =>
        {
            try
            {
                string? line;
                while ((line = await _process.StandardError.ReadLineAsync(_disposeCts.Token).ConfigureAwait(false)) is not null)
                    StderrLineReceived?.Invoke(line);
            }
            catch { }
        });

        _ = Task.Run(async () =>
        {
            try { await ReadLoopAsync(_disposeCts.Token).ConfigureAwait(false); }
            catch (Exception) { }
            finally { CancelAllPendingRequests(); }
        });
    }

    public async Task<JsonNode?> SendRequestAsync(string method, JsonNode? parameters, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return null;

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var message = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };
        if (parameters is not null)
            message["params"] = parameters;

        try
        {
            await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(id, out var orphan);
            orphan?.TrySetResult(null);
            if (cancellationToken.IsCancellationRequested)
                return null;
            throw;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);
        using var _ = timeoutCts.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var pending))
                pending.TrySetResult(null);
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task SendNotificationAsync(string method, JsonNode? parameters, CancellationToken cancellationToken = default)
    {
        var message = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
        };
        if (parameters is not null)
            message["params"] = parameters;

        await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteMessageAsync(JsonObject message, CancellationToken cancellationToken)
    {
        var json = message.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {bytes.Length}\r\n\r\n";

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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

            DispatchMessage(JsonNode.Parse(json));
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

    public async Task SendResponseAsync(int id, JsonNode? result, CancellationToken cancellationToken = default)
    {
        var message = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"]      = id,
            ["result"]  = result,
        };
        await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private void DispatchMessage(JsonNode? node)
    {
        if (node is null)
            return;

        var idNode = node["id"];
        var method = node["method"]?.GetValue<string>();

        // Response to a prior request (our pending TCS holds it)
        if (idNode is not null && TryGetIntId(idNode, out var numericId) &&
            _pending.TryRemove(numericId, out var tcs))
        {
            var error = node["error"];
            if (error is not null)
                tcs.TrySetException(new InvalidOperationException(error["message"]?.GetValue<string>() ?? "LSP error"));
            else
                tcs.TrySetResult(node["result"]);
            return;
        }

        // Notification or server-initiated request
        if (method is not null)
        {
            int? requestId = idNode is not null && TryGetIntId(idNode, out var rid) ? rid : null;
            NotificationReceived?.Invoke(method, node["params"], requestId);
        }
    }

    private static bool TryGetIntId(JsonNode idNode, out int id)
    {
        try
        {
            id = idNode.GetValue<int>();
            return true;
        }
        catch
        {
            id = 0;
            return false;
        }
    }

    private void CancelAllPendingRequests()
    {
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var tcs))
                tcs.TrySetCanceled();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        try
        {
            if (_process is { HasExited: false })
                _trackedProcess?.KillTree();
        }
        catch { }
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
        if (_trackedProcess is not null)
            await _trackedProcess.DisposeAsync().ConfigureAwait(false);
        _trackedProcess = null;
        _process = null;
        _disposeCts.Dispose();
        _writeGate.Dispose();
    }
}
