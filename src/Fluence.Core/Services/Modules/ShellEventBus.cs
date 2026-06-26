using System;
using System.Collections.Generic;
using System.Threading;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.Output;

namespace Fluence.Core.Services.Modules;

public sealed class ShellEventBus : IShellEventBus
{
    private readonly Lock _lock = new();
    private readonly IUiDispatcher? _dispatcher;
    private readonly IOutputChannelService? _output;
    private readonly Dictionary<Type, Action<IShellEvent>[]> _syncHandlers = new();
    private readonly Dictionary<object, Action<IShellEvent>> _syncWrappers = new();

    public ShellEventBus() { }

    public ShellEventBus(IUiDispatcher dispatcher) { _dispatcher = dispatcher; }

    public ShellEventBus(IUiDispatcher dispatcher, IOutputChannelService output)
    {
        _dispatcher = dispatcher;
        _output = output;
    }

    public void Publish(IShellEvent shellEvent)
    {
        var type = shellEvent.GetType();
        Action<IShellEvent>[] snapshot;
        lock (_lock)
        {
            snapshot = _syncHandlers.TryGetValue(type, out var handlers)
                ? handlers
                : [];
        }

        foreach (var handler in snapshot)
            DispatchSync(handler, shellEvent);
    }

    public IDisposable SubscribeSync<TEvent>(Action<TEvent> handler) where TEvent : IShellEvent
    {
        var type = typeof(TEvent);
        Action<IShellEvent> wrapper = e => handler((TEvent)e);
        lock (_lock)
        {
            _syncWrappers[handler] = wrapper;
            if (!_syncHandlers.TryGetValue(type, out var handlers))
            {
                _syncHandlers[type] = [wrapper];
                return new Subscription<TEvent>(this, handler);
            }

            var updated = new Action<IShellEvent>[handlers.Length + 1];
            Array.Copy(handlers, updated, handlers.Length);
            updated[^1] = wrapper;
            _syncHandlers[type] = updated;
        }

        return new Subscription<TEvent>(this, handler);
    }

    public void UnsubscribeSync<TEvent>(Action<TEvent> handler) where TEvent : IShellEvent
    {
        var type = typeof(TEvent);
        lock (_lock)
        {
            if (!_syncHandlers.TryGetValue(type, out var handlers))
                return;
            if (_syncWrappers.TryGetValue(handler, out var wrapper))
            {
                _syncWrappers.Remove(handler);
                var index = Array.IndexOf(handlers, wrapper);
                if (index < 0)
                    return;

                if (handlers.Length == 1)
                {
                    _syncHandlers.Remove(type);
                    return;
                }

                var updated = new Action<IShellEvent>[handlers.Length - 1];
                if (index > 0)
                    Array.Copy(handlers, 0, updated, 0, index);
                if (index < handlers.Length - 1)
                    Array.Copy(handlers, index + 1, updated, index, handlers.Length - index - 1);
                _syncHandlers[type] = updated;
            }
        }
    }

    private void DispatchSync(Action<IShellEvent> handler, IShellEvent shellEvent)
    {
        if (_dispatcher is not null)
        {
            if (_dispatcher.CheckAccess())
            {
                InvokeSync(handler, shellEvent);
                return;
            }

            _dispatcher.Post(() => InvokeSync(handler, shellEvent));
            return;
        }

        InvokeSync(handler, shellEvent);
    }

    private void InvokeSync(Action<IShellEvent> handler, IShellEvent shellEvent)
    {
        try
        {
            handler(shellEvent);
        }
        catch (Exception ex)
        {
            var message = $"[ShellEventBus] Handler failed for {shellEvent.GetType().Name}: {ex.Message}{Environment.NewLine}";
            if (_output is not null)
                _ = _output.WriteAsync(OutputChannelIds.Output, message, OutputLogLevel.Error);
        }
    }

    private sealed class Subscription<TEvent>(ShellEventBus owner, Action<TEvent> handler) : IDisposable
        where TEvent : IShellEvent
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            owner.UnsubscribeSync(handler);
        }
    }
}
