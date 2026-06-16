using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Modules;
using Fluence.Modules.LanguageServer.Services;

namespace Fluence.Modules.LanguageServer;

public sealed partial class Entrypoint
{
    private readonly object _pendingSemanticGate = new();
    private string? _pendingSemanticFilePath;
    private Timer? _pendingSemanticTimer;

    private void QueueSemanticTokens(string filePath)
    {
        lock (_pendingSemanticGate)
        {
            _pendingSemanticFilePath = filePath;
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
        string? filePath;
        lock (_pendingSemanticGate)
        {
            filePath = _pendingSemanticFilePath;
            _pendingSemanticFilePath = null;
        }

        if (filePath is null || _semanticTokensService is null || _eventBus is null) return;
        SafeSend(SendSemanticTokensAsync(_semanticTokensService, _eventBus, filePath));
    }

    private static async Task SendSemanticTokensAsync(SemanticTokensService service, IShellEventBus events, string filePath)
    {
        try
        {
            var tokens = await service.RequestAsync(filePath, CancellationToken.None).ConfigureAwait(false);
            if (tokens.Length > 0)
                events.Publish(new SemanticTokensUpdatedEvent(filePath, tokens));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[LS] semanticTokens failed: {ex.Message}"); }
    }
}
