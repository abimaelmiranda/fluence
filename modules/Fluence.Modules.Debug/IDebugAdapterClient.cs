using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Workspace;

namespace Fluence.Modules.Debug;

internal interface IDebugAdapterClient
{
    Task StartAsync(ProjectExecutionTarget target, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
