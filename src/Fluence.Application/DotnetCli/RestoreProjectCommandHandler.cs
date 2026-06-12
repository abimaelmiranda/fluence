using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;

namespace Fluence.Application.DotnetCli;

public sealed class RestoreProjectCommandHandler(ITerminalService terminal)
    : DotnetProjectCommandHandlerBase(terminal), ICommandHandler<RestoreProjectCommand>
{
    public Task HandleAsync(RestoreProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("restore", command.ProjectPath, cancellationToken);
}
