using System;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Storage;

namespace Fluence.Infrastructure.Protocols.Dap;

public sealed class DapDebugAdapterClientFactory(NetcoredbgToolService tools, IFluenceStorageService storage) : IDebugAdapterClientFactory
{
    public async Task<IDebugAdapterClient> CreateAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        var executable = await tools.ResolveAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var logPath = storage.GetProjectPath(workspaceRoot, $"logs/dap-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");
        return new DapClient(executable, logPath);
    }
}
