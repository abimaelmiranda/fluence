namespace Fluence.Core.Abstractions.Debugging;

public interface IDebugAdapterClientFactory
{
    Task<IDebugAdapterClient> CreateAsync(
        string workspaceRoot,
        string adapterExecutable,
        CancellationToken cancellationToken = default);
}
