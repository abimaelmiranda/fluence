using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;

namespace Fluence.Modules.DotnetCli;

public sealed class RunSpecificProjectCommandHandler(ITerminalService terminal)
    : DotnetProjectCommandHandlerBase(terminal), ICommandHandler<RunSpecificProjectCommand>
{
    public Task HandleAsync(RunSpecificProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("run --project", command.ProjectPath, cancellationToken);
}
