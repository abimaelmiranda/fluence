using System;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
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

    public string Id => "Terminal";

    public string DisplayName => "Terminal";

    public int StartupOrder => 600;

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<TerminalViewModel>();
    }

    public ModuleContributions GetContributions() =>
        new()
        {
            Panels =
            [
                new ShellPanelContribution(
                    ShellRegion.BottomBar,
                    Id,
                    "Terminal",
                    services => services.GetRequiredService<TerminalViewModel>(),
                    PanelVisibilityRule.Always),
            ],
        };

    public Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _terminalService = host.Services.GetRequiredService<ITerminalService>();
        host.SetModuleState(Id, ModuleState.Active);
        return Task.CompletedTask;
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
