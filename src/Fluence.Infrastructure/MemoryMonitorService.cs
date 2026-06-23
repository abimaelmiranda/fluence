using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Settings;
using Fluence.Core.Services;

namespace Fluence.Infrastructure;

public sealed class MemoryMonitorService : IAsyncDisposable
{
    private readonly ISettingsService _settings;
    private readonly IOutputChannelService _output;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Lock _stateLock = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private IDisposable? _settingsSubscription;

    public MemoryMonitorService(ISettingsService settings, IOutputChannelService output)
    {
        _settings = settings;
        _output = output;
    }

    public void Start()
    {
        var current = _settings.Get<DiagnosticsSettings>();
        ApplySettings(current);

        _settingsSubscription = _settings.Watch<DiagnosticsSettings>().Subscribe(
            new ActionObserver<DiagnosticsSettings>(
                onNext: ApplySettings,
                onError: _ => { }));
    }

    private void ApplySettings(DiagnosticsSettings settings)
    {
        lock (_stateLock)
        {
            if (settings.EnableMemoryMonitor && _loopTask is null)
            {
                _loopCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
                _loopTask = Task.Run(() => RunAsync(settings.MemoryMonitorIntervalSeconds, _loopCts.Token));
            }
            else if (!settings.EnableMemoryMonitor && _loopTask is not null)
            {
                _loopCts?.Cancel();
                _loopTask = null;
                _loopCts = null;
            }
        }
    }

    private async Task RunAsync(int intervalSeconds, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, intervalSeconds));
        var proc = Process.GetCurrentProcess();

        try
        {
            await LogAsync(proc, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
                proc.Refresh();
                await LogAsync(proc, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MemoryMonitor] {ex.Message}");
        }
    }

    private Task LogAsync(Process proc, CancellationToken ct)
    {
        var ws = proc.WorkingSet64 / 1_048_576;
        var gc = GC.GetTotalMemory(forceFullCollection: false) / 1_048_576;
        var level = ws > 1_500 ? OutputLogLevel.Warning : OutputLogLevel.Debug;
        var msg = $"WorkingSet={ws}MB  GC.Heap={gc}MB  {DateTimeOffset.Now:HH:mm:ss}{Environment.NewLine}";
        return _output.WriteAsync(OutputChannelIds.Memory, msg, level, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _settingsSubscription?.Dispose();
        _disposeCts.Cancel();

        Task? runningLoop;
        lock (_stateLock)
            runningLoop = _loopTask;

        if (runningLoop is not null)
        {
            try { await runningLoop.ConfigureAwait(false); }
            catch { }
        }

        _loopCts?.Dispose();
        _disposeCts.Dispose();
    }
}
