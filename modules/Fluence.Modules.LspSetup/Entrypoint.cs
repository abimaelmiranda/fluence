using System;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Languages;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Modules.LspSetup.ViewModels;
using Fluence.Core.Events.Provisioning;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.LspSetup;

public sealed class Entrypoint : IModule, IConditionalModule
{
    private IDisposable? _provisioningSubscription;

    public string Id => "LspSetup";

    public bool ShouldActivate(IWorkspaceContext workspace, ILanguageProfileRegistry profiles)
    {
        var languageId = profiles.DetectWorkspaceLanguage(workspace);
        return languageId is null or "csharp" or "c" or "cpp";
    }

    public string DisplayName => "LSP Setup";

    public const string ChannelId = "lsp-setup";

    public int StartupOrder => 800;

    public ModuleContributions GetContributions() =>
        new() { OutputChannel = new OutputChannelDescriptor(ChannelId, "LSP Setup") };

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<LspSetupViewModel>();
    }

    public Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        host.Services.GetRequiredService<ILocalizationService>()
            .Register(new ResourceManager(
                "Fluence.Modules.LspSetup.Resources.Strings",
                typeof(Entrypoint).Assembly));

        var scheduler = host.Services.GetRequiredService<ITaskScheduler>();

        _provisioningSubscription = host.Events.SubscribeSync<LspProvisioningRequiredEvent>(e =>
        {
            var vm = host.Services.GetRequiredService<LspSetupViewModel>();
            host.Workspace.OpenToolTab("tool://fluence/lsp-setup", "Language Server Setup", vm);
            scheduler.Schedule(
                "lsp.provision",
                TaskPriority.Background,
                ct => vm.StartProvisioningAsync(ct));
        });

        host.SetModuleState(Id, ModuleState.Active);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _provisioningSubscription?.Dispose();
        _provisioningSubscription = null;
        return ValueTask.CompletedTask;
    }
}
