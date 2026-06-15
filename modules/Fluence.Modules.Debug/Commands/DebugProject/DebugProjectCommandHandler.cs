using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Services.Debugging;

namespace Fluence.Modules.Debug.Commands.DebugProject;

public sealed class DebugProjectCommandHandler(IDebugService debug)
    : ICommandHandler<DebugProjectCommand>
{
    public async Task HandleAsync(DebugProjectCommand command, CancellationToken cancellationToken = default)
    {
        await debug.StartAsync(cancellationToken);
    }
}
