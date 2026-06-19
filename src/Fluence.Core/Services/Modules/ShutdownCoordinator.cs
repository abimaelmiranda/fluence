using System.Diagnostics;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Lifecycle;
using Fluence.Core.Models.Modules;
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
        CancellationToken cancellationToken = default) =>
        await ShutdownAsync(ApplicationShutdownReason.ApplicationQuit, progress, cancellationToken).ConfigureAwait(false);

    public async Task ShutdownAsync(
        ApplicationShutdownReason reason,
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
                if (module is IModuleShutdownParticipant participant)
                {
                    try
                    {
                        var context = new ModuleShutdownContext(reason, cancellationToken);
                        await participant.StopAsync(context)
                            .WaitAsync(ModuleShutdownTimeout, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
                    {
                        Debug.WriteLine($"Module {module.Name} stop canceled after {started.ElapsedMilliseconds}ms: {ex.Message}");
                    }
                    catch (TimeoutException ex)
                    {
                        Debug.WriteLine($"Module {module.Name} stop timed out after {started.ElapsedMilliseconds}ms: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Module {module.Name} stop failed after {started.ElapsedMilliseconds}ms: {ex}");
                        host.SetModuleState(module.Name, ModuleState.Faulted);
                    }
                }

                try
                {
                    await module.DisposeAsync()
                        .AsTask()
                        .WaitAsync(ModuleShutdownTimeout, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
                {
                    Debug.WriteLine($"Module {module.Name} dispose canceled after {started.ElapsedMilliseconds}ms: {ex.Message}");
                }
                catch (TimeoutException ex)
                {
                    Debug.WriteLine($"Module {module.Name} dispose timed out after {started.ElapsedMilliseconds}ms: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Module {module.Name} dispose failed after {started.ElapsedMilliseconds}ms: {ex}");
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
