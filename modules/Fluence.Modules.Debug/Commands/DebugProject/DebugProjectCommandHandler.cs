using Fluence.Core.Commands;
using Fluence.Core.Debug;

namespace Fluence.Modules.Debug.Commands.DebugProject;

public sealed class DebugProjectCommandHandler(IDebugService debug)
    : ICommandHandler<DebugProjectCommand>
{
    public async Task HandleAsync(DebugProjectCommand command, CancellationToken cancellationToken = default)
    {
        await debug.StartAsync(cancellationToken);
    }
}
