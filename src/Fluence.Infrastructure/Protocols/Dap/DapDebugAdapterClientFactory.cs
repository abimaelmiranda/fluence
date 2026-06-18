using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Storage;

namespace Fluence.Infrastructure.Protocols.Dap;

public sealed class DapDebugAdapterClientFactory(
    IDebuggerProvisioningService provisioning,
    IFluenceStorageService storage,
    IProcessSpawner spawner) : IDebugAdapterClientFactory
{
    public Task<IDebugAdapterClient> CreateAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!provisioning.IsProvisioned())
        {
            throw new FileNotFoundException(
                "No global .NET debugger was found. Use the debugger provisioning flow to install it before debugging.",
                provisioning.GetExecutablePath());
        }

        var executable = provisioning.GetExecutablePath();
        var logPath = storage.GetProjectPath(workspaceRoot, $"logs/dap-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");
        return Task.FromResult<IDebugAdapterClient>(new DapClient(executable, logPath, spawner));
    }
}
