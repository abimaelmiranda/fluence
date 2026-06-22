using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Languages;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Workspace;
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
    private readonly Lock _lock = new();
    private readonly List<IModule> _pendingActivation = [];
    private readonly HashSet<string> _activatedModuleIds = [];
    private IModuleHost? _host;
    private IWorkspaceContext? _workspace;
    private ILanguageProfileRegistry? _profiles;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await CleanupPreviousSessionBestEffort(cancellationToken);

        await InitializeModulesAsync(cancellationToken);
        RegisterActiveModuleContributions();
        shellRegions.Refresh();

        lock (_lock)
        {
            if (_pendingActivation.Count > 0 && _workspace is not null)
                _workspace.Changed += OnWorkspaceChanged;
        }
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

    private void RegisterActiveModuleContributions()
    {
        HashSet<string> activeIds;
        lock (_lock)
            activeIds = [.. _activatedModuleIds];

        var panels = modules
            .Where(m => activeIds.Contains(m.Id))
            .OrderBy(static m => m.StartupOrder)
            .SelectMany(static m => m.GetContributions().Panels)
            .ToArray();

        shellRegions.RegisterPanels(panels);
    }

    private async Task InitializeModulesAsync(CancellationToken cancellationToken)
    {
        var host = services.GetRequiredService<IModuleHost>();
        var workspace = services.GetRequiredService<IWorkspaceContext>();
        var profiles = services.GetRequiredService<ILanguageProfileRegistry>();

        _host = host;
        _workspace = workspace;
        _profiles = profiles;

        foreach (var module in modules.OrderBy(static m => m.StartupOrder))
        {
            if (module is IConditionalModule conditional && !conditional.ShouldActivate(workspace, profiles))
            {
                host.SetModuleState(module.Id, ModuleState.Disabled);
                lock (_lock)
                    _pendingActivation.Add(module);
                continue;
            }

            await TryInitializeModuleAsync(module, host, cancellationToken);
        }
    }

    private async Task TryInitializeModuleAsync(IModule module, IModuleHost host, CancellationToken cancellationToken)
    {
        try
        {
            await module.InitializeAsync(host, cancellationToken);
            lock (_lock)
                _activatedModuleIds.Add(module.Id);
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

    // Fires on UI thread via IWorkspaceContext.Changed → _dispatcher.Post()
    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        _ = TryActivatePendingModulesAsync();
    }

    private async Task TryActivatePendingModulesAsync()
    {
        if (_host is null || _workspace is null || _profiles is null)
            return;

        List<IModule> toActivate;
        lock (_lock)
        {
            toActivate = _pendingActivation
                .Where(m => m is IConditionalModule c && c.ShouldActivate(_workspace, _profiles))
                .OrderBy(static m => m.StartupOrder)
                .ToList();

            foreach (var m in toActivate)
                _pendingActivation.Remove(m);
        }

        if (toActivate.Count == 0)
            return;

        foreach (var module in toActivate)
            await TryInitializeModuleAsync(module, _host, CancellationToken.None);

        RegisterActiveModuleContributions();
        shellRegions.Refresh();

        lock (_lock)
        {
            if (_pendingActivation.Count == 0 && _workspace is not null)
                _workspace.Changed -= OnWorkspaceChanged;
        }
    }
}
