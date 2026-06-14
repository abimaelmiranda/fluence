using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Workspace;

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