using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Problems;
using Fluence.Core.Models.Problems;
using Fluence.Core.Services.Problems;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.DotnetCli.Abstractions.Commands;
using Fluence.Modules.DotnetCli.Services;

namespace Fluence.Modules.DotnetCli.Commands.Workspace.Build;

public sealed class BuildWorkspaceCommandHandler(
    IWorkspaceContext workspace,
    IProcessHost processHost,
    IOutputChannelService output,
    IDotnetSdkProvisioningService sdk,
    IProblemService problems)
    : DotnetCommandHandlerBase(workspace, processHost, output, sdk), ICommandHandler<BuildWorkspaceCommand>
{
    public async Task HandleAsync(BuildWorkspaceCommand command, CancellationToken cancellationToken = default)
    {
        var buildProblems = new List<ProblemItem>();
        var buildProblemsGate = new object();
        problems.ClearSource(ProblemSourceIds.Build);
        await RunDotnetAsync(
            "build",
            cancellationToken,
            (line, workingDirectory) =>
            {
                var problem = MsBuildProblemParser.TryParse(line, workingDirectory);
                if (problem is not null)
                {
                    lock (buildProblemsGate)
                        buildProblems.Add(problem);
                }
            });
        problems.ReplaceSource(ProblemSourceIds.Build, buildProblems);
    }
}
