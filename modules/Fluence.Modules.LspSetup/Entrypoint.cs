using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Modules.LspSetup.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.LspSetup;

public sealed class Entrypoint : IModule
{
    public string Name => "LspSetup";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<LspSetupViewModel>();
    }

    public void Initialize(IModuleHost host)
    {
        var scheduler = host.Services.GetRequiredService<ITaskScheduler>();

        host.Events.SubscribeSync<LspProvisioningRequiredEvent>(e =>
        {
            var vm = host.Services.GetRequiredService<LspSetupViewModel>();
            host.Workspace.OpenToolTab("tool://fluence/lsp-setup", "Language Server Setup", vm);
            scheduler.Schedule(
                "lsp.provision",
                TaskPriority.Background,
                ct => vm.StartProvisioningAsync(ct));
        });

        host.SetModuleState(Name, ModuleState.Active);
    }
}
