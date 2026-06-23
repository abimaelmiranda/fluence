using System.Diagnostics;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Models.Lifecycle;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Services.Modules;

public sealed class ShutdownCoordinator(
    IModuleHost host,
    IReadOnlyList<IModule> modules,
    IProcessSpawner processSpawner,
    IOutputChannelService output) : IShutdownCoordinator
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

            foreach (var module in modules.OrderByDescending(static module => module.StartupOrder))
            {
                progress?.Report(new ModuleShutdownProgress($"Stopping {module.DisplayName}..."));
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
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                    }
                    catch (TimeoutException)
                    {
                        _ = output.WriteAsync(OutputChannelIds.Output,
                            $"[shutdown] {module.DisplayName} stop timed out after {started.ElapsedMilliseconds}ms{Environment.NewLine}",
                            OutputLogLevel.Warning, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        host.SetModuleState(module.Id, ModuleState.Faulted);
                        _ = output.WriteAsync(OutputChannelIds.Output,
                            $"[shutdown] {module.DisplayName} stop failed: {ex.Message}{Environment.NewLine}",
                            OutputLogLevel.Error, CancellationToken.None);
                    }
                }

                try
                {
                    await module.DisposeAsync()
                        .AsTask()
                        .WaitAsync(ModuleShutdownTimeout, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (TimeoutException)
                {
                    Debug.WriteLine($"Module {module.Id} dispose timed out after {started.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    host.SetModuleState(module.Id, ModuleState.Faulted);
                    Debug.WriteLine($"Module {module.Id} dispose failed after {started.ElapsedMilliseconds}ms: {ex}");
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
