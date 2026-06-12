using System;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Infrastructure;

public interface ITerminalService
{
    event EventHandler<TerminalLineEventArgs>? LineReceived;
    event EventHandler? Cleared;

    bool IsBusy { get; }
    bool HasActiveSession { get; }
    IPtySession? ActiveSession { get; }

    Task StartShellAsync(string? workingDirectory = null, int columns = 80, int rows = 24, CancellationToken cancellationToken = default);

    Task SendInputAsync(string text, CancellationToken cancellationToken = default);

    Task ResizeAsync(int columns, int rows);

    Task ExecuteAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    Task CancelAsync();

    void Clear();
}
