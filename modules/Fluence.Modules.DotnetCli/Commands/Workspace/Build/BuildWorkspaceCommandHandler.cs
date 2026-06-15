using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Models.Infrastructure;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Workspace.Build;

public sealed class BuildWorkspaceCommandHandler(IWorkspaceContext workspace, ITerminalService terminal)
    : DotnetCommandHandlerBase(workspace, terminal), ICommandHandler<BuildWorkspaceCommand>
{
    public Task HandleAsync(BuildWorkspaceCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("build", cancellationToken);
}
