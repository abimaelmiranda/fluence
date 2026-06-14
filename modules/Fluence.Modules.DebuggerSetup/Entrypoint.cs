using Fluence.Core.Modules.Abstractions;
using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Fluence.Modules.DebuggerSetup.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.DebuggerSetup;

public sealed class Entrypoint : IModule
{
    public string Name => "DebuggerSetup";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<DebuggerSetupViewModel>();
    }

    public void Initialize(IModuleHost host)
    {
        host.Events.Subscribe<DebuggerProvisioningRequiredEvent>(e =>
        {
            var vm = host.Services.GetRequiredService<DebuggerSetupViewModel>();
            host.Workspace.OpenToolTab("tool://fluence/debugger-setup", "Debugger Setup", vm);
            var _ = vm.StartProvisioningAsync();
        });

        host.SetModuleState(Name, ModuleState.Active);
    }
}