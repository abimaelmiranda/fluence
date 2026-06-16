using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Modules;
using Fluence.Modules.LanguageServer.Services;

namespace Fluence.Modules.LanguageServer;

public sealed partial class Entrypoint
{
    private const int MaxSemanticTokenAttempts = 8;
    private readonly object _pendingSemanticGate = new();
    private readonly HashSet<string> _pendingSemanticFilePaths = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _pendingSemanticTimer;

    private void QueueSemanticTokens(string filePath)
    {
        lock (_pendingSemanticGate)
        {
            _pendingSemanticFilePaths.Add(filePath);
            _pendingSemanticTimer ??= new Timer(
                static state => ((Entrypoint)state!).OnSemanticTokensTimerElapsed(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _pendingSemanticTimer.Change(SemanticTokensDebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnSemanticTokensTimerElapsed()
    {
        string[] filePaths;
        lock (_pendingSemanticGate)
        {
            if (_pendingSemanticFilePaths.Count == 0)
                return;

            filePaths = [.. _pendingSemanticFilePaths];
            _pendingSemanticFilePaths.Clear();
        }

        if (_semanticTokensService is null || _eventBus is null) return;
        foreach (var filePath in filePaths)
            SafeSend(SendSemanticTokensAsync(_semanticTokensService, _eventBus, filePath));
    }

    private async Task SendSemanticTokensAsync(SemanticTokensService service, IShellEventBus events, string filePath)
    {
        try
        {
            if (!ShouldRequestSemanticTokens(filePath))
                return;

            var tokens = await service.RequestAsync(filePath, CancellationToken.None).ConfigureAwait(false);
            if (tokens.Length == 0)
            {
                if (TryScheduleSemanticRetry(filePath))
                    QueueSemanticTokens(filePath);
                else
                    events.Publish(new SemanticTokensRefreshFailedEvent(filePath));
                return;
            }

            MarkSemanticTokensFinished(filePath);
            events.Publish(new SemanticTokensUpdatedEvent(filePath, tokens));
            events.Publish(new SemanticTokensRefreshFinishedEvent(filePath));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MarkSemanticTokensFinished(filePath);
            events.Publish(new SemanticTokensRefreshFailedEvent(filePath));
            Debug.WriteLine($"[LS] semanticTokens failed: {ex.Message}");
        }
    }

    private bool ShouldRequestSemanticTokens(string filePath)
    {
        lock (_documentGate)
        {
            return _documents.TryGetValue(filePath, out var state) &&
                   state.IsOpenInServer &&
                   state.TokensPending;
        }
    }

    private bool TryScheduleSemanticRetry(string filePath)
    {
        lock (_documentGate)
        {
            if (!_documents.TryGetValue(filePath, out var state) || !state.TokensPending)
                return false;

            state.SemanticAttempt++;
            if (state.SemanticAttempt < MaxSemanticTokenAttempts)
                return true;

            state.TokensPending = false;
            return false;
        }
    }

    private void MarkSemanticTokensFinished(string filePath)
    {
        lock (_documentGate)
        {
            if (!_documents.TryGetValue(filePath, out var state))
                return;

            state.TokensPending = false;
            state.SemanticAttempt = 0;
        }
    }
}
