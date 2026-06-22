using Fluence.Core.Abstractions.Languages;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Abstractions.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Infrastructure.Languages.Routers;

public sealed class LspArgumentsBuilderRouter(
    IKeyedServiceProvider keyedProvider,
    IWorkspaceContext workspace,
    ILanguageProfileRegistry profiles) : ILspArgumentsBuilder
{
    private string ActiveKey => profiles.DetectWorkspaceLanguage(workspace) ?? "csharp";

    private ILspArgumentsBuilder Active =>
        keyedProvider.GetRequiredKeyedService<ILspArgumentsBuilder>(ActiveKey);

    public string Build(string rootPath, ILspProvisioningService provisioning, ISettingsService settings) =>
        Active.Build(rootPath, provisioning, settings);
}
