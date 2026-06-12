using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;

namespace Fluence.Application.DotnetCli;

public sealed class CleanProjectCommandHandler(ITerminalService terminal)
    : DotnetProjectCommandHandlerBase(terminal), ICommandHandler<CleanProjectCommand>
{
    public Task HandleAsync(CleanProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("clean", command.ProjectPath, cancellationToken);
}
