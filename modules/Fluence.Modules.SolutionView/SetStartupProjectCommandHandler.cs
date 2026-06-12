using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Workspace;

namespace Fluence.Modules.SolutionView;

public sealed class SetStartupProjectCommandHandler(IWorkspaceContext workspace)
    : ICommandHandler<SetStartupProjectCommand>
{
    public Task HandleAsync(SetStartupProjectCommand command, CancellationToken cancellationToken = default)
    {
        workspace.SetStartupProject(command.ProjectPath);
        return Task.CompletedTask;
    }
}
