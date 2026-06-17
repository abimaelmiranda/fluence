using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Modules.LanguageServer.Services;

namespace Fluence.Modules.LanguageServer;

public sealed partial class Entrypoint
{
    private const int MaxSemanticTokenAttempts = 8;
    private int _semanticRequestSeq;

    private void QueueSemanticTokens(string filePath)
    {
        var seq = Interlocked.Increment(ref _semanticRequestSeq);
        _scheduler!.ScheduleLatest(
            $"lsp.semantic.{filePath}",
            TaskPriority.Background,
            SemanticTokensDebounceDelay,
            ct => _semanticTokensService is null || _eventBus is null
                ? Task.CompletedTask
                : SendSemanticTokensAsync(_semanticTokensService, _eventBus, filePath, ct),
            correlationId: seq);
    }

    private void CancelSemanticTokens(string filePath)
    {
        var seq = Interlocked.Increment(ref _semanticRequestSeq);
        _scheduler!.Cancel($"lsp.semantic.{filePath}");
        _eventBus?.Publish(new SemanticTokensRefreshFinishedEvent(filePath));
    }

    private async Task SendSemanticTokensAsync(SemanticTokensService service, IShellEventBus events, string filePath, CancellationToken ct)
    {
        if (!ShouldRequestSemanticTokens(filePath))
            return;

        events.Publish(new SemanticTokensRefreshStartedEvent(filePath));
        var tokens = await service.RequestAsync(filePath, ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested)
        {
            events.Publish(new SemanticTokensRefreshFinishedEvent(filePath));
            return;
        }

        if (tokens.Length == 0)
        {
            if (TryScheduleSemanticRetry(filePath))
            {
                events.Publish(new SemanticTokensRefreshFinishedEvent(filePath));
                QueueSemanticTokens(filePath);
            }
            else
            {
                events.Publish(new SemanticTokensRefreshFailedEvent(filePath));
            }
            return;
        }

        MarkSemanticTokensFinished(filePath);
        events.Publish(new SemanticTokensUpdatedEvent(filePath, tokens));
        events.Publish(new SemanticTokensRefreshFinishedEvent(filePath));
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
