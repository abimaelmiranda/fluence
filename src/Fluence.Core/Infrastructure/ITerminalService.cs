using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Infrastructure;

public interface ITerminalService
{
    event EventHandler? SessionsChanged;

    int MaxSessions { get; }
    bool IsBusy { get; }
    bool HasActiveSession { get; }
    bool CanCreateSession { get; }
    IReadOnlyList<ITerminalSession> Sessions { get; }
    ITerminalSession? ActiveSession { get; }

    ITerminalSession CreateSession();

    Task CloseSessionAsync(ITerminalSession session);

    void SetActiveSession(ITerminalSession session);

    Task ExecuteAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);
}
