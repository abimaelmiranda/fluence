using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Storage;

namespace Fluence.Infrastructure;

public sealed class ProcessSpawner(IFluenceStorageService storage) : IProcessSpawner
{
    private const string SessionFileName = "session.json";
    private static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<int, TrackedProcess> _processes = new();
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private int _killAllCompleted;
    private bool _disposed;

    public async Task<ITrackedProcess> StartAsync(
        ProcessStartInfo startInfo,
        string owner,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"Unable to start process '{startInfo.FileName}'.");
        }

        var tracked = new TrackedProcess(this, process, owner);
        _processes[process.Id] = tracked;
        process.Exited += (_, _) => ObserveException(RemoveAsync(process.Id, persist: true), $"RemoveAsync({process.Id})");
        await PersistSessionAsync(cancellationToken).ConfigureAwait(false);
        return tracked;
    }

    public async Task CleanupPreviousSessionAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadSessionAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot?.Processes is not { Count: > 0 })
            return;

        foreach (var item in snapshot.Processes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var process = Process.GetProcessById(item.Pid);
                if (!process.HasExited && IsPersistedProcessMatch(process, item))
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Previous-session cleanup is best-effort.
            }
        }

        await PersistSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task KillAllAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _killAllCompleted, 1) == 1)
            return;

        foreach (var process in _processes.Values.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.KillTree();
        }

        foreach (var process in _processes.Values.ToArray())
        {
            try
            {
                await process.Process.WaitForExitAsync(cancellationToken)
                    .WaitAsync(ProcessExitTimeout, cancellationToken)
                    .ConfigureAwait(false);
                process.DisposeProcess();
            }
            catch
            {
                // Shutdown fallback must continue through all tracked processes.
            }
        }

        _processes.Clear();
        await PersistSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await KillAllAsync(CancellationToken.None).ConfigureAwait(false);
        _sessionGate.Dispose();
    }

    private async Task RemoveAsync(int pid, bool persist)
    {
        _processes.TryRemove(pid, out _);

        if (persist)
            await PersistSessionAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task PersistSessionAsync(CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = new ProcessSessionSnapshot(
                Environment.ProcessId,
                DateTimeOffset.UtcNow,
                _processes.Values
                    .Where(process => !process.Process.HasExited)
                    .Select(process => new TrackedProcessSnapshot(
                        process.Id,
                        process.Owner,
                        process.Process.StartInfo.FileName,
                        ReadStartTimeUtc(process.Process)))
                    .ToArray());

            var path = storage.GetUserPath(SessionFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(
                stream,
                snapshot,
                ProcessSpawnerJsonContext.Default.ProcessSessionSnapshot,
                cancellationToken).ConfigureAwait(false);
        }
        catch when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Process persistence must not fail process startup/shutdown.
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task<ProcessSessionSnapshot?> ReadSessionAsync(CancellationToken cancellationToken)
    {
        var path = storage.GetUserPath(SessionFileName);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(
                stream,
                ProcessSpawnerJsonContext.Default.ProcessSessionSnapshot,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private sealed class TrackedProcess(ProcessSpawner owner, Process process, string ownerName) : ITrackedProcess
    {
        private int _disposed;

        public int Id => process.Id;
        public string Owner { get; } = ownerName;
        public Process Process => process;

        public void KillTree()
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }

        public void Untrack()
        {
            ObserveException(owner.RemoveAsync(Id, persist: true), $"Untrack({Id})");
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            KillTree();
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(ProcessExitTimeout)
                    .ConfigureAwait(false);
            }
            catch
            {
            }

            await owner.RemoveAsync(Id, persist: true).ConfigureAwait(false);
            DisposeProcess();
        }

        public void DisposeProcess()
        {
            try { process.Dispose(); } catch { }
        }
    }

    private static bool IsPersistedProcessMatch(Process process, TrackedProcessSnapshot snapshot)
    {
        var currentStartTime = ReadStartTimeUtc(process);
        if (currentStartTime is null || snapshot.StartTimeUtc is null)
            return false;

        return currentStartTime.Value == snapshot.StartTimeUtc.Value &&
               IsExecutableMatch(process, snapshot.Executable);
    }

    private static bool IsExecutableMatch(Process process, string executable)
    {
        try
        {
            var processFileName = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(processFileName) || string.IsNullOrWhiteSpace(executable))
                return false;

            return string.Equals(
                       Path.GetFileName(processFileName),
                       Path.GetFileName(executable),
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(processFileName, executable, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void ObserveException(Task task, string context)
    {
        task.ContinueWith(
            t => Debug.WriteLine($"[ProcessSpawner] {context}: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    private static DateTimeOffset? ReadStartTimeUtc(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return null;
        }
    }
}

[JsonSerializable(typeof(ProcessSessionSnapshot))]
internal sealed partial class ProcessSpawnerJsonContext : JsonSerializerContext;

internal sealed record ProcessSessionSnapshot(
    int ParentPid,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TrackedProcessSnapshot> Processes);

internal sealed record TrackedProcessSnapshot(
    int Pid,
    string Owner,
    string Executable,
    DateTimeOffset? StartTimeUtc);
