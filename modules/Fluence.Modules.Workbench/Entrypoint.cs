using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Workspace.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Workbench;

public sealed class Entrypoint : IModule, IModuleShutdownParticipant
{
    private readonly IModule[] _modules =
    [
        new Fluence.Modules.FileExplorer.Entrypoint(),
        new Fluence.Modules.SolutionView.Entrypoint(),
        new Fluence.Modules.Editor.Entrypoint(),
        new Fluence.Modules.XamlViewer.Entrypoint(),
    ];

    public string Id => "Workbench";

    public string DisplayName => "Workbench";

    public int StartupOrder => 200;

    public void Register(IServiceCollection services)
    {
        foreach (var module in _modules)
            module.Register(services);
    }

    public ModuleContributions GetContributions() =>
        new()
        {
            Panels = _modules
                .OrderBy(static module => module.StartupOrder)
                .SelectMany(static module => module.GetContributions().Panels)
                .ToArray(),
        };

    public async Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken)
    {
        var outputRegistry = host.Services.GetRequiredService<IOutputChannelRegistry>();

        foreach (var module in _modules.OrderBy(static module => module.StartupOrder))
        {
            if (module.GetContributions().OutputChannel is { } channel)
                outputRegistry.Register(channel);
            await module.InitializeAsync(host, cancellationToken).ConfigureAwait(false);
        }

        host.SetModuleState(Id, ModuleState.Active);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var module in _modules.OrderByDescending(static module => module.StartupOrder))
            await module.DisposeAsync().ConfigureAwait(false);
    }

    public async Task StopAsync(ModuleShutdownContext context)
    {
        foreach (var module in _modules.OrderByDescending(static module => module.StartupOrder))
            if (module is IModuleShutdownParticipant participant)
                await participant.StopAsync(context).ConfigureAwait(false);
    }
}
