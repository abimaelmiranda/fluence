using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;

namespace Fluence.Modules.Workbench.SolutionView.Commands.SetStartupProject;

public sealed class SetStartupProjectCommandHandler(IWorkspaceContext workspace)
    : ICommandHandler<SetStartupProjectCommand>
{
    public Task HandleAsync(SetStartupProjectCommand command, CancellationToken cancellationToken = default)
    {
        workspace.SetStartupProject(command.ProjectPath);
        return Task.CompletedTask;
    }
}