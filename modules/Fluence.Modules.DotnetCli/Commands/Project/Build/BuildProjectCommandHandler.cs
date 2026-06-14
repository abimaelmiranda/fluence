using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Project.Build;

public sealed class BuildProjectCommandHandler(ITerminalService terminal)
    : DotnetProjectCommandHandlerBase(terminal), ICommandHandler<BuildProjectCommand>
{
    public Task HandleAsync(BuildProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("build", command.ProjectPath, cancellationToken);
}
