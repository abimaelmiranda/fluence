using System;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Models.Output;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Models.Workbench;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.Terminal.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Terminal;

public sealed class Entrypoint : IModule, IModuleShutdownParticipant
{
    private ITerminalService? _terminalService;

    public string Id => "Terminal";

    public string DisplayName => "Terminal";

    public const string ChannelId = "terminal";

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
                    PanelVisibilityRule.ForBottomBarTab(BottomBarTabIds.Terminal)),
            ],
            OutputChannel = new OutputChannelDescriptor(ChannelId, "Terminal"),
        };

    public Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        host.Services.GetRequiredService<ILocalizationService>()
            .Register(new ResourceManager("Fluence.Modules.Terminal.Resources.Strings", typeof(Entrypoint).Assembly));
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
