using System;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Languages;
using Fluence.Core.Abstractions.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Infrastructure.Languages.Routers;

public sealed class DebuggerProvisioningRouter(
    IServiceProvider serviceProvider,
    IWorkspaceContext workspace,
    ILanguageProfileRegistry profiles) : IDebuggerProvisioningService
{
    private string ActiveKey => profiles.DetectWorkspaceLanguage(workspace) ?? "csharp";

    private IDebuggerProvisioningService Active =>
        serviceProvider.GetRequiredKeyedService<IDebuggerProvisioningService>(ActiveKey);

    public bool IsProvisioned() => Active.IsProvisioned();
    public string GetExecutablePath() => Active.GetExecutablePath();
    public Task ProvisionAsync(Action<string> onOutput, CancellationToken cancellationToken = default) =>
        Active.ProvisionAsync(onOutput, cancellationToken);
}
