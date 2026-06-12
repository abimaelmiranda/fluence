using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;

namespace Fluence.Application.DotnetCli;

public sealed class BuildProjectCommandHandler(ITerminalService terminal)
    : DotnetProjectCommandHandlerBase(terminal), ICommandHandler<BuildProjectCommand>
{
    public Task HandleAsync(BuildProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("build", command.ProjectPath, cancellationToken);
}
