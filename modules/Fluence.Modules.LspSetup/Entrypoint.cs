using System;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Modules.LspSetup.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.LspSetup;

public sealed class Entrypoint : IModule
{
    private IDisposable? _provisioningSubscription;

    public string Id => "LspSetup";

    public string DisplayName => "LSP Setup";

    public int StartupOrder => 800;

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<LspSetupViewModel>();
    }

    public Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
