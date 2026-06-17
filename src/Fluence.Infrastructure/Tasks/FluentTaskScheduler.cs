using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Tasks;

namespace Fluence.Infrastructure.Tasks;

public sealed class FluentTaskScheduler : ITaskScheduler, IDisposable
{
    private readonly ConcurrentDictionary<string, OwnerEntry> _owners = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<ScheduledWork> _criticalQueue = new();
    private readonly ConcurrentQueue<ScheduledWork> _inputQueue = new();
    private readonly ConcurrentQueue<ScheduledWork> _interactiveQueue = new();
    private readonly ConcurrentQueue<ScheduledWork> _backgroundQueue = new();
    private readonly ConcurrentQueue<ScheduledWork> _maintenanceQueue = new();
    private readonly SemaphoreSlim _highPrioritySignal = new(0);
    private readonly SemaphoreSlim _lowPrioritySignal = new(0);
    private readonly CancellationTokenSource _disposeCts = new();
    private int _highPriorityActive;
    private int _highPriorityQueued;

    private sealed class OwnerEntry
    {
        public CancellationTokenSource Cts = new();
        public object? CorrelationId;
    }

    private sealed record ScheduledWork(TaskPriority Priority, Func<CancellationToken, Task> Work, CancellationToken Token);

    public FluentTaskScheduler()
    {
        _ = Task.Run(ProcessHighPriorityQueueAsync);
        _ = Task.Run(ProcessLowPriorityQueueAsync);
    }

    public void Schedule(string ownerId, TaskPriority priority, Func<CancellationToken, Task> work, object? correlationId = null)
    {
        var token = GetOrRefreshToken(ownerId, correlationId);
        Enqueue(new ScheduledWork(priority, work, token));
    }

    public void ScheduleLatest(
        string ownerId,
        TaskPriority priority,
        TimeSpan delay,
        Func<CancellationToken, Task> work,
        object? correlationId = null)
    {
        var token = ReplaceToken(ownerId, correlationId);
        _ = Task.Run(async () =>
        {
            try
            {
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, token).ConfigureAwait(false);

                if (!token.IsCancellationRequested)
                    Enqueue(new ScheduledWork(priority, work, token));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Scheduler] ScheduleLatest failed: {ex.Message}");
            }
        });
    }

    public void Cancel(string ownerId)
    {
        if (!_owners.TryGetValue(ownerId, out var entry))
            return;

        lock (entry)
        {
            entry.Cts.Cancel();
        }
    }

    private void Enqueue(ScheduledWork item)
    {
        var priority = item.Priority;
        if (priority <= TaskPriority.Interactive)
        {
            Interlocked.Increment(ref _highPriorityQueued);
            if (priority == TaskPriority.Critical)
                _criticalQueue.Enqueue(item);
            else if (priority == TaskPriority.Input)
                _inputQueue.Enqueue(item);
            else
                _interactiveQueue.Enqueue(item);
            _highPrioritySignal.Release();
            return;
        }

        if (priority == TaskPriority.Background)
            _backgroundQueue.Enqueue(item);
        else
            _maintenanceQueue.Enqueue(item);
        _lowPrioritySignal.Release();
    }

    private CancellationToken GetOrRefreshToken(string ownerId, object? correlationId)
    {
        var entry = _owners.GetOrAdd(ownerId, _ => new OwnerEntry());
        lock (entry)
        {
            if (!Equals(entry.CorrelationId, correlationId))
            {
                entry.Cts.Cancel();
                entry.Cts.Dispose();
                entry.Cts = new CancellationTokenSource();
                entry.CorrelationId = correlationId;
            }
            return entry.Cts.Token;
        }
    }

    private CancellationToken ReplaceToken(string ownerId, object? correlationId)
    {
        var entry = _owners.GetOrAdd(ownerId, _ => new OwnerEntry());
        lock (entry)
        {
            entry.Cts.Cancel();
            entry.Cts.Dispose();
            entry.Cts = new CancellationTokenSource();
            entry.CorrelationId = correlationId;
            return entry.Cts.Token;
        }
    }

    private async Task ProcessHighPriorityQueueAsync()
    {
        while (!_disposeCts.IsCancellationRequested)
        {
            try
            {
                await _highPrioritySignal.WaitAsync(_disposeCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!TryDequeueHighPriority(out var item))
                continue;

            Interlocked.Decrement(ref _highPriorityQueued);
            await RunWithPriorityAsync(item).ConfigureAwait(false);
        }
    }

    private async Task ProcessLowPriorityQueueAsync()
    {
        while (!_disposeCts.IsCancellationRequested)
        {
            try
            {
                await _lowPrioritySignal.WaitAsync(_disposeCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (Volatile.Read(ref _highPriorityQueued) > 0 || Volatile.Read(ref _highPriorityActive) > 0)
            {
                try
                {
                    await Task.Delay(16, _disposeCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            if (!TryDequeueLowPriority(out var item))
                continue;

            await RunWithPriorityAsync(item).ConfigureAwait(false);
        }
    }

    private bool TryDequeueHighPriority(out ScheduledWork item) =>
        _criticalQueue.TryDequeue(out item!) ||
        _inputQueue.TryDequeue(out item!) ||
        _interactiveQueue.TryDequeue(out item!);

    private bool TryDequeueLowPriority(out ScheduledWork item) =>
        _backgroundQueue.TryDequeue(out item!) ||
        _maintenanceQueue.TryDequeue(out item!);

    private async Task RunWithPriorityAsync(ScheduledWork item)
    {
        var priority = item.Priority;
        var ct = item.Token;
        var isHighPriority = priority <= TaskPriority.Interactive;
        if (isHighPriority)
            Interlocked.Increment(ref _highPriorityActive);

        try
        {
            if (ct.IsCancellationRequested) return;
            await item.Work(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Scheduler] task failed: {ex.Message}");
        }
        finally
        {
            if (isHighPriority)
                Interlocked.Decrement(ref _highPriorityActive);
        }
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        foreach (var entry in _owners.Values)
        {
            lock (entry)
            {
                entry.Cts.Cancel();
                entry.Cts.Dispose();
            }
        }
        _disposeCts.Dispose();
        _highPrioritySignal.Dispose();
        _lowPrioritySignal.Dispose();
    }
}
