using System.Diagnostics;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Services.Modules;

public sealed class ShutdownCoordinator(
    IModuleHost host,
    IReadOnlyList<IModule> modules,
    IProcessSpawner processSpawner) : IShutdownCoordinator
{
    private static readonly TimeSpan ModuleShutdownTimeout = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _shutdownComplete;

    public bool IsShutdownComplete => Volatile.Read(ref _shutdownComplete);

    public async Task ShutdownAsync(
        IProgress<ModuleShutdownProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsShutdownComplete)
            return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsShutdownComplete)
                return;

            foreach (var module in modules.Reverse())
            {
                progress?.Report(new ModuleShutdownProgress($"Stopping {module.Name}..."));
                var started = Stopwatch.StartNew();
                try
                {
                    await module.DisposeAsync()
                        .AsTask()
                        .WaitAsync(ModuleShutdownTimeout, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Module {module.Name} shutdown failed after {started.ElapsedMilliseconds}ms: {ex}");
                    host.SetModuleState(module.Name, ModuleState.Faulted);
                }
            }

            progress?.Report(new ModuleShutdownProgress("Stopping remaining processes..."));
            try
            {
                await processSpawner.KillAllAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Tracked process shutdown failed/timed out: {ex}");
            }

            Volatile.Write(ref _shutdownComplete, true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
