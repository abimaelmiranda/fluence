using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Models.Infrastructure;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Workspace.Clean;

public sealed class CleanWorkspaceCommandHandler(
    IWorkspaceContext workspace,
    ITerminalService terminal,
    IDotnetSdkProvisioningService sdk)
    : DotnetCommandHandlerBase(workspace, terminal, sdk), ICommandHandler<CleanWorkspaceCommand>
{
    public Task HandleAsync(CleanWorkspaceCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("clean", cancellationToken);
}
