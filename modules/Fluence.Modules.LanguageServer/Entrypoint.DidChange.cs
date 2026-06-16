using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;

namespace Fluence.Modules.LanguageServer;

public sealed partial class Entrypoint
{
    private readonly object _pendingDidChangeGate = new();
    private PendingDidChange? _pendingDidChange;
    private DateTime? _pendingDidChangeDueAtUtc;
    private Timer? _pendingDidChangeTimer;
    private Task? _pendingDidChangeSendTask;

    private void QueueDidChange(string filePath, int version, string content)
    {
        lock (_pendingDidChangeGate)
        {
            _pendingDidChange = new PendingDidChange(filePath, content, version);
            _pendingDidChangeDueAtUtc = DateTime.UtcNow + DidChangeDebounceDelay;
            _pendingDidChangeTimer ??= new Timer(
                static state => ((Entrypoint)state!).OnDidChangeTimerElapsed(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _pendingDidChangeTimer.Change(DidChangeDebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task FlushPendingDidChangeImmediatelyAsync(ILanguageServerService lsp, string filePath)
    {
        PendingDidChange? pending;
        Task? inFlightTask;
        lock (_pendingDidChangeGate)
        {
            _pendingDidChangeTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            pending = _pendingDidChange is not null &&
                      string.Equals(_pendingDidChange.FilePath, filePath, StringComparison.OrdinalIgnoreCase)
                ? _pendingDidChange
                : null;

            if (pending is not null)
            {
                _pendingDidChange = null;
                _pendingDidChangeDueAtUtc = null;
            }

            inFlightTask = _pendingDidChangeSendTask;
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
                await lsp.SendDidChangeAsync(pending.FilePath, pending.Version, pending.Content, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"[LS] flush didChange failed: {ex.Message}"); }
        }
    }

    private async Task FlushPendingDidChangeAndCloseAsync(ILanguageServerService lsp, string filePath)
    {
        PendingDidChange? pending;
        Task? inFlightTask;
        lock (_pendingDidChangeGate)
        {
            _pendingDidChangeTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            pending = _pendingDidChange is not null &&
                      string.Equals(_pendingDidChange.FilePath, filePath, StringComparison.OrdinalIgnoreCase)
                ? _pendingDidChange
                : null;

            if (pending is not null)
            {
                _pendingDidChange = null;
                _pendingDidChangeDueAtUtc = null;
            }

            inFlightTask = _pendingDidChangeSendTask;
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
                await lsp.SendDidChangeAsync(pending.FilePath, pending.Version, pending.Content, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"[LS] close flush didChange failed: {ex.Message}"); }
        }

        try
        {
            await lsp.SendDidCloseAsync(filePath, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[LS] didClose failed: {ex.Message}"); }
    }

    private void OnDidChangeTimerElapsed()
    {
        lock (_pendingDidChangeGate)
        {
            if (_pendingDidChange is null || _pendingDidChangeDueAtUtc is null)
                return;

            var remaining = _pendingDidChangeDueAtUtc.Value - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                _pendingDidChangeTimer?.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }

            var pending = _pendingDidChange;
            _pendingDidChange = null;
            _pendingDidChangeDueAtUtc = null;

            if (pending is null || _languageServer is null)
                return;

            _pendingDidChangeSendTask = SendPendingDidChangeAsync(_languageServer, pending);
        }
    }

    private async Task SendPendingDidChangeAsync(ILanguageServerService lsp, PendingDidChange pending)
    {
        try
        {
            await lsp.SendDidChangeAsync(pending.FilePath, pending.Version, pending.Content, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[LS] sendPendingDidChange failed: {ex.Message}"); }
    }

    private sealed record PendingDidChange(string FilePath, string Content, int Version);
}
