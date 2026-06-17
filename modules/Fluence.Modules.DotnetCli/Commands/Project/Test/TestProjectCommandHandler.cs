using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Models.Infrastructure;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Project.Test;

public sealed class TestProjectCommandHandler(ITerminalService terminal, IDotnetSdkProvisioningService sdk)
    : DotnetProjectCommandHandlerBase(terminal, sdk), ICommandHandler<TestProjectCommand>
{
    public Task HandleAsync(TestProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("test", command.ProjectPath, cancellationToken);
}
