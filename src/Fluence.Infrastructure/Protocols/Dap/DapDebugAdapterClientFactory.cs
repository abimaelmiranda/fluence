using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Services.Debugging;

namespace Fluence.Infrastructure.Protocols.Dap;

public sealed class DapDebugAdapterClientFactory(NetcoredbgToolService tools) : IDebugAdapterClientFactory
{
    public async Task<IDebugAdapterClient> CreateAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        var executable = await tools.ResolveAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var logPath = Path.Combine(
            workspaceRoot,
            ".fluence",
            "logs",
            $"dap-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");
        return new DapClient(executable, logPath);
    }
}
