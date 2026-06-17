using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Tasks;

namespace Fluence.Modules.LanguageServer;

public sealed partial class Entrypoint
{
    private readonly object _pendingDidChangeGate = new();
    private readonly Dictionary<string, PendingDidChange> _pendingDidChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task> _pendingDidChangeSendTasks = new(StringComparer.OrdinalIgnoreCase);

    private void QueueDidChange(string filePath, int version, string content)
    {
        lock (_pendingDidChangeGate)
        {
            _pendingDidChanges[filePath] = new PendingDidChange(filePath, content, version);
        }

        _scheduler!.ScheduleLatest(
            $"lsp.didchange.{filePath}",
            TaskPriority.Input,
            DidChangeDebounceDelay,
            ct => SendLatestDidChangeAsync(filePath, ct),
            correlationId: version);
    }

    private async Task FlushPendingDidChangeImmediatelyAsync(ILanguageServerService lsp, string filePath, CancellationToken ct)
    {
        PendingDidChange? pending;
        Task? inFlightTask;
        _scheduler?.Cancel($"lsp.didchange.{filePath}");
        lock (_pendingDidChangeGate)
        {
            _pendingDidChanges.Remove(filePath, out pending);
            _pendingDidChangeSendTasks.TryGetValue(filePath, out inFlightTask);
        }

        if (inFlightTask is not null)
        {
            try { await inFlightTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"[LS] flush in-flight didChange failed: {ex.Message}"); }
        }

        if (pending is not null)
        {
            try
            {
                await lsp.SendDidChangeAsync(pending.FilePath, pending.Version, pending.Content, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"[LS] flush didChange failed: {ex.Message}"); }
        }
    }

    private async Task FlushPendingDidChangeAndCloseAsync(ILanguageServerService lsp, string filePath, CancellationToken ct)
    {
        PendingDidChange? pending;
        Task? inFlightTask;
        _scheduler?.Cancel($"lsp.didchange.{filePath}");
        lock (_pendingDidChangeGate)
        {
            _pendingDidChanges.Remove(filePath, out pending);
            _pendingDidChangeSendTasks.TryGetValue(filePath, out inFlightTask);
        }

        if (inFlightTask is not null)
        {
            try { await inFlightTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"[LS] close flush in-flight didChange failed: {ex.Message}"); }
        }

        if (pending is not null)
        {
            try
            {
                await lsp.SendDidChangeAsync(pending.FilePath, pending.Version, pending.Content, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"[LS] close flush didChange failed: {ex.Message}"); }
        }

        try
        {
            await lsp.SendDidCloseAsync(filePath, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[LS] didClose failed: {ex.Message}"); }
    }

    private async Task SendLatestDidChangeAsync(string filePath, CancellationToken ct)
    {
        if (_languageServer is null)
            return;

        PendingDidChange? pending;
        lock (_pendingDidChangeGate)
        {
            if (!_pendingDidChanges.Remove(filePath, out pending))
                return;
        }

        var task = SendPendingDidChangeAsync(_languageServer, pending, ct);
        lock (_pendingDidChangeGate)
            _pendingDidChangeSendTasks[pending.FilePath] = task;
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            lock (_pendingDidChangeGate)
            {
                if (_pendingDidChangeSendTasks.TryGetValue(pending.FilePath, out var current) &&
                    ReferenceEquals(current, task))
                {
                    _pendingDidChangeSendTasks.Remove(pending.FilePath);
                }
            }
        }
    }

    private async Task SendPendingDidChangeAsync(ILanguageServerService lsp, PendingDidChange pending, CancellationToken ct)
    {
        try
        {
            await lsp.SendDidChangeAsync(pending.FilePath, pending.Version, pending.Content, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[LS] sendPendingDidChange failed: {ex.Message}"); }
    }

    private sealed record PendingDidChange(string FilePath, string Content, int Version);
}
