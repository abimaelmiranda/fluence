using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;

namespace Fluence.Modules.SolutionView.Commands.OpenSolution;

public sealed class OpenSolutionWorkspaceCommandHandler(IWorkspaceContext workspace)
    : ICommandHandler<OpenSolutionWorkspaceCommand>
{
    public Task HandleAsync(OpenSolutionWorkspaceCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        workspace.OpenSolution(command.Path);
        return Task.CompletedTask;
    }
}