using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Fluence.Modules.DebuggerSetup.ViewModels;
using Fluence.Modules.DebuggerSetup.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.DebuggerSetup;

public sealed class DebuggerSetupModule : IIdeModule
{
    public string Name => "DebuggerSetup";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<DebuggerSetupViewModel>();
    }

    public void RegisterViews(IViewRegistry registry)
    {
        registry.Register<DebuggerSetupViewModel, DebuggerSetupView>();
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
