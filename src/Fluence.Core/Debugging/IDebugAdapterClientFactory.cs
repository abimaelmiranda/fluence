namespace Fluence.Core.Debug;

public interface IDebugAdapterClientFactory
{
    Task<IDebugAdapterClient> CreateAsync(string workspaceRoot, CancellationToken cancellationToken = default);
}
