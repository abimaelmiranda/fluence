using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Languages;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Infrastructure.Languages.Routers;

public sealed class LspProvisioningRouter(
    IKeyedServiceProvider keyedProvider,
    IWorkspaceContext workspace,
    ILanguageProfileRegistry profiles) : ILspProvisioningService
{
    private string ActiveKey => profiles.DetectWorkspaceLanguage(workspace) ?? "csharp";

    private ILspProvisioningService Active =>
        keyedProvider.GetRequiredKeyedService<ILspProvisioningService>(ActiveKey);

    public bool IsProvisioned() => Active.IsProvisioned();
    public string GetExecutablePath() => Active.GetExecutablePath();
    public IReadOnlyDictionary<string, string> GetLaunchEnvironment() => Active.GetLaunchEnvironment();
    public Task ProvisionAsync(Action<string> onOutput, CancellationToken cancellationToken = default) =>
        Active.ProvisionAsync(onOutput, cancellationToken);
}
