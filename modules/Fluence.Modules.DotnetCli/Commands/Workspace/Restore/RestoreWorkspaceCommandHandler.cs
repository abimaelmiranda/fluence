using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Workspace.Restore;

public sealed class RestoreWorkspaceCommandHandler(
    IWorkspaceContext workspace,
    IProcessHost processHost,
    IOutputChannelService output,
    IDotnetSdkProvisioningService sdk)
    : DotnetCommandHandlerBase(workspace, processHost, output, sdk), ICommandHandler<RestoreWorkspaceCommand>
{
    public Task HandleAsync(RestoreWorkspaceCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("restore", cancellationToken);
}
