using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Workspace.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Desktop.Composition;

internal sealed class ApplicationStartupCoordinator(
    IServiceProvider services,
    IReadOnlyList<IModule> modules,
    IShellRegionHost shellRegions,
    IProcessSpawner processSpawner,
    IOutputChannelService output) : IStartupCoordinator
{
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await CleanupPreviousSessionBestEffort(cancellationToken);

        RegisterModuleContributions();
        await InitializeModulesAsync(cancellationToken);
        shellRegions.Refresh();
    }

    private async Task CleanupPreviousSessionBestEffort(CancellationToken cancellationToken)
    {
        try
        {
            await processSpawner.CleanupPreviousSessionAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await output.WriteAsync(
                    OutputChannelIds.Output,
                    $"[startup] Previous-session process cleanup failed: {ex.Message}{Environment.NewLine}",
                    OutputChannelEntryKind.Warning,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Debug.WriteLine($"Previous-session process cleanup failed: {ex}");
        }
    }

    private void RegisterModuleContributions()
    {
        var panels = modules
            .OrderBy(static module => module.StartupOrder)
            .SelectMany(static module => module.GetContributions().Panels)
            .ToArray();

        shellRegions.RegisterPanels(panels);
    }

    private async Task InitializeModulesAsync(CancellationToken cancellationToken)
    {
        var host = services.GetRequiredService<IModuleHost>();

        foreach (var module in modules.OrderBy(static module => module.StartupOrder))
        {
            try
            {
                await module.InitializeAsync(host, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                host.SetModuleState(module.Id, ModuleState.Faulted);
                throw;
            }
            catch (Exception ex)
            {
                host.SetModuleState(module.Id, ModuleState.Faulted);
                await output.WriteAsync(
                    OutputChannelIds.Output,
                    $"[startup] Module initialization failed. Id='{module.Id}', DisplayName='{module.DisplayName}', StartupOrder={module.StartupOrder}: {ex.Message}{Environment.NewLine}",
                    OutputChannelEntryKind.Error,
                    CancellationToken.None);
                Debug.WriteLine($"Module {module.Id} failed to initialize: {ex}");
            }
        }
    }
}
