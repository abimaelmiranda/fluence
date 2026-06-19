using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Modules.LanguageServer.Services;
using Fluence.Core.Events.Lsp;

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
        if (!TryGetSemanticTokenRequestVersion(filePath, out var requestVersion))
            return;

        events.Publish(new SemanticTokensRefreshStartedEvent(filePath));
        var tokens = await service.RequestAsync(filePath, ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested)
        {
            events.Publish(new SemanticTokensRefreshFinishedEvent(filePath));
            return;
        }

        if (!IsSemanticTokenVersionCurrent(filePath, requestVersion))
        {
            events.Publish(new SemanticTokensRefreshFinishedEvent(filePath));
            return;
        }

        if (tokens.Length == 0)
        {
            if (TryScheduleSemanticRetry(filePath, requestVersion, out var versionCurrent))
            {
                events.Publish(new SemanticTokensRefreshFinishedEvent(filePath));
                QueueSemanticTokens(filePath);
            }
            else
            {
                if (versionCurrent)
                    events.Publish(new SemanticTokensRefreshFailedEvent(filePath));
                else
                    events.Publish(new SemanticTokensRefreshFinishedEvent(filePath));
            }
            return;
        }

        if (!MarkSemanticTokensFinished(filePath, requestVersion))
        {
            events.Publish(new SemanticTokensRefreshFinishedEvent(filePath));
            return;
        }

        events.Publish(new SemanticTokensUpdatedEvent(filePath, requestVersion, tokens));
        events.Publish(new SemanticTokensRefreshFinishedEvent(filePath));
    }

    private bool TryGetSemanticTokenRequestVersion(string filePath, out int version)
    {
        lock (_documentGate)
        {
            if (_documents.TryGetValue(filePath, out var state) &&
                state.IsOpenInServer &&
                state.TokensPending)
            {
                version = state.Version;
                return true;
            }
        }

        version = 0;
        return false;
    }

    private bool IsSemanticTokenVersionCurrent(string filePath, int version)
    {
        lock (_documentGate)
        {
            return _documents.TryGetValue(filePath, out var state) &&
                   state.IsOpenInServer &&
                   state.TokensPending &&
                   state.Version == version;
        }
    }

    private bool TryScheduleSemanticRetry(string filePath, int version, out bool versionCurrent)
    {
        lock (_documentGate)
        {
            if (!_documents.TryGetValue(filePath, out var state) ||
                state.Version != version)
            {
                versionCurrent = false;
                return false;
            }

            versionCurrent = true;
            if (!state.TokensPending)
                return false;

            state.SemanticAttempt++;
            if (state.SemanticAttempt < MaxSemanticTokenAttempts)
                return true;

            state.TokensPending = false;
            return false;
        }
    }

    private bool MarkSemanticTokensFinished(string filePath, int version)
    {
        lock (_documentGate)
        {
            if (!_documents.TryGetValue(filePath, out var state))
                return false;
            if (state.Version != version)
                return false;

            state.TokensPending = false;
            state.SemanticAttempt = 0;
            return true;
        }
    }
}
