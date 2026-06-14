using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Project.Restore;

public sealed class RestoreProjectCommandHandler(ITerminalService terminal)
    : DotnetProjectCommandHandlerBase(terminal), ICommandHandler<RestoreProjectCommand>
{
    public Task HandleAsync(RestoreProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("restore", command.ProjectPath, cancellationToken);
}
