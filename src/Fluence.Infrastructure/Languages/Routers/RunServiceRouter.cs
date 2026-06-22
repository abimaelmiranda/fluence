using System;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Languages;
using Fluence.Core.Abstractions.Projects;
using Fluence.Core.Abstractions.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Infrastructure.Languages.Routers;

public sealed class RunServiceRouter(
    IServiceProvider serviceProvider,
    IWorkspaceContext workspace,
    ILanguageProfileRegistry profiles) : IRunService
{
    private string ActiveKey => profiles.DetectWorkspaceLanguage(workspace) ?? "csharp";

    private IRunService Active =>
        serviceProvider.GetRequiredKeyedService<IRunService>(ActiveKey);

    public Task RunAsync(CancellationToken cancellationToken = default) =>
        Active.RunAsync(cancellationToken);

    public Task RunSpecificAsync(string projectPath, CancellationToken cancellationToken = default) =>
        Active.RunSpecificAsync(projectPath, cancellationToken);
}
