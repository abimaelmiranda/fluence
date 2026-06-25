using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Storage;

namespace Fluence.Infrastructure.Protocols.Dap;

public sealed class DapDebugAdapterClientFactory(
    IFluenceStorageService storage,
    IProcessSpawner spawner) : IDebugAdapterClientFactory
{
    public Task<IDebugAdapterClient> CreateAsync(
        string workspaceRoot,
        string adapterExecutable,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(adapterExecutable))
            throw new FileNotFoundException("Debug adapter executable was not found.", adapterExecutable);

        var logPath = storage.GetProjectPath(workspaceRoot, $"logs/dap-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");
        var adapterId = Path.GetFileNameWithoutExtension(adapterExecutable);
        return Task.FromResult<IDebugAdapterClient>(new DapClient(adapterExecutable, adapterId, logPath, spawner));
    }
}
