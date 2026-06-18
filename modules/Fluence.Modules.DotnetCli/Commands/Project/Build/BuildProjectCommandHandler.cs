using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Problems;
using Fluence.Core.Models.Problems;
using Fluence.Modules.DotnetCli.Abstractions.Commands;
using Fluence.Modules.DotnetCli.Services;

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
        problems.ClearSource("Build");
        await RunDotnetAsync(
            "build",
            command.ProjectPath,
            cancellationToken,
            (line, workingDirectory) =>
            {
                var problem = BuildProblemParser.TryParse(line, workingDirectory);
                if (problem is not null)
                    buildProblems.Add(problem);
            });
        problems.ReplaceSource("Build", buildProblems);
    }
}
