using System;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.Terminal.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Terminal;

public sealed class Entrypoint : IModule, IModuleShutdownParticipant
{
    private ITerminalService? _terminalService;

    public string Name => "Terminal";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<TerminalViewModel>();
    }

    public void Initialize(IModuleHost host)
    {
        _terminalService = host.Services.GetRequiredService<ITerminalService>();
        host.ShellRegions.SetContent(
            ShellRegion.BottomBar,
            Name,
            "Terminal",
            host.Services.GetRequiredService<TerminalViewModel>());
        host.SetModuleState(Name, ModuleState.Active);
    }

    public async ValueTask DisposeAsync()
    {
        var service = _terminalService;
        _terminalService = null;
        if (service is IAsyncDisposable asyncDisposable)
        {
            try
            {
                await asyncDisposable.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Terminal module shutdown failed/timed out: {ex}");
            }
        }
    }

    public Task StopAsync(ModuleShutdownContext context)
    {
        var service = _terminalService;
        _terminalService = null;
        if (service is IAsyncDisposable asyncDisposable)
            return asyncDisposable.DisposeAsync().AsTask().WaitAsync(context.CancellationToken);
        return Task.CompletedTask;
    }
}
