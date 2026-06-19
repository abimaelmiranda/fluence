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
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Project.Build;

public sealed class BuildProjectCommandHandler(
    IProcessHost processHost,
    IOutputChannelService output,
    IDotnetSdkProvisioningService sdk,
    IProblemService problems)
    : DotnetProjectCommandHandlerBase(processHost, output, sdk), ICommandHandler<BuildProjectCommand>
{
    public async Task HandleAsync(BuildProjectCommand command, CancellationToken cancellationToken = default)
    {
        var buildProblems = new List<ProblemItem>();
        var buildProblemsGate = new object();
        problems.ClearSource(ProblemSourceIds.Build);
        await RunDotnetAsync(
            "build",
            command.ProjectPath,
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
