using System.Diagnostics;

namespace Fluence.Core.Abstractions.Infrastructure;

public interface IProcessSpawner : IAsyncDisposable
{
    Task<ITrackedProcess> StartAsync(
        ProcessStartInfo startInfo,
        string owner,
        CancellationToken cancellationToken = default);

    Task CleanupPreviousSessionAsync(CancellationToken cancellationToken = default);

    Task KillAllAsync(CancellationToken cancellationToken = default);
}

public interface ITrackedProcess : IAsyncDisposable
{
    int Id { get; }
    string Owner { get; }
    Process Process { get; }
    void KillTree();
    void Untrack();
}

public interface IProcessGroup : IAsyncDisposable
{
    void Add(Process process);
    void Kill();
}
