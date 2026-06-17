using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Models.Infrastructure;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Workspace.Test;

public sealed class TestWorkspaceCommandHandler(
    IWorkspaceContext workspace,
    ITerminalService terminal,
    IDotnetSdkProvisioningService sdk)
    : DotnetCommandHandlerBase(workspace, terminal, sdk), ICommandHandler<TestWorkspaceCommand>
{
    public Task HandleAsync(TestWorkspaceCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("test", cancellationToken);
}
