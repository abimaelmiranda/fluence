using System;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.DebuggerSetup.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.DebuggerSetup;

public sealed class Entrypoint : IModule
{
    private IDisposable? _provisioningSubscription;

    public string Name => "DebuggerSetup";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<DebuggerSetupViewModel>();
    }

    public void Initialize(IModuleHost host)
    {
        var scheduler = host.Services.GetRequiredService<ITaskScheduler>();

        _provisioningSubscription = host.Events.SubscribeSync<DebuggerProvisioningRequiredEvent>(e =>
        {
            var vm = host.Services.GetRequiredService<DebuggerSetupViewModel>();
            host.Workspace.OpenToolTab("tool://fluence/debugger-setup", "Debugger Setup", vm);
            scheduler.Schedule(
                "debugger.provision",
                TaskPriority.Background,
                ct => vm.StartProvisioningAsync(ct));
        });

        host.SetModuleState(Name, ModuleState.Active);
    }

    public ValueTask DisposeAsync()
    {
        _provisioningSubscription?.Dispose();
        _provisioningSubscription = null;
        return ValueTask.CompletedTask;
    }
}
