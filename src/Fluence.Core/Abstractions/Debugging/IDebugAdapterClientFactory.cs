namespace Fluence.Core.Abstractions.Debugging;

public interface IDebugAdapterClientFactory
{
    Task<IDebugAdapterClient> CreateAsync(string workspaceRoot, CancellationToken cancellationToken = default);
}
