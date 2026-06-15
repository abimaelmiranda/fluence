using System;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.Infrastructure;

namespace Fluence.Core.Abstractions.Infrastructure;

public interface ITerminalSession : IAsyncDisposable
{
    event EventHandler<TerminalDataEventArgs>? DataReceived;
    event EventHandler? Cleared;

    bool HasStarted { get; }
    bool HasExited { get; }
    bool IsClosing { get; }
    bool CanAcceptInput { get; }

    Task StartShellAsync(
        string? workingDirectory = null,
        int columns = 80,
        int rows = 24,
        CancellationToken cancellationToken = default);

    Task SendInputAsync(string text, CancellationToken cancellationToken = default);

    Task ResizeAsync(int columns, int rows);

    Task ExecuteAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    Task CancelAsync();

    void Clear();
}
