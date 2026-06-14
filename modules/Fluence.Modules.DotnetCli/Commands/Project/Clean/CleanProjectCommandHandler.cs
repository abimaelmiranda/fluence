using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Project.Clean;

public sealed class CleanProjectCommandHandler(ITerminalService terminal)
    : DotnetProjectCommandHandlerBase(terminal), ICommandHandler<CleanProjectCommand>
{
    public Task HandleAsync(CleanProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("clean", command.ProjectPath, cancellationToken);
}
