using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Models.Infrastructure;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Project.Restore;

public sealed class RestoreProjectCommandHandler(ITerminalService terminal)
    : DotnetProjectCommandHandlerBase(terminal), ICommandHandler<RestoreProjectCommand>
{
    public Task HandleAsync(RestoreProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("restore", command.ProjectPath, cancellationToken);
}
