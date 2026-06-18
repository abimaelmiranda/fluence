using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;

namespace Fluence.Core.Services.Modules;

public sealed class ShellEventBus : IShellEventBus
{
    private readonly Lock _lock = new();
    private readonly IUiDispatcher? _dispatcher;
    private readonly Dictionary<Type, List<Action<IShellEvent>>> _syncHandlers = new();
    private readonly Dictionary<object, Action<IShellEvent>> _syncWrappers = new();

    public ShellEventBus() { }

    public ShellEventBus(IUiDispatcher dispatcher) { _dispatcher = dispatcher; }

    public void Publish(IShellEvent shellEvent)
    {
        var type = shellEvent.GetType();
        Action<IShellEvent>[] snapshot;
        lock (_lock)
        {
            snapshot = _syncHandlers.TryGetValue(type, out var handlers)
                ? handlers.ToArray()
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
            if (!_syncHandlers.TryGetValue(type, out var list))
                _syncHandlers[type] = list = [];
            _syncWrappers[handler] = wrapper;
            list.Add(wrapper);
        }

        return new Subscription<TEvent>(this, handler);
    }

    public void UnsubscribeSync<TEvent>(Action<TEvent> handler) where TEvent : IShellEvent
    {
        var type = typeof(TEvent);
        lock (_lock)
        {
            if (!_syncHandlers.TryGetValue(type, out var list))
                return;
            if (_syncWrappers.TryGetValue(handler, out var wrapper))
            {
                list.Remove(wrapper);
                _syncWrappers.Remove(handler);
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

    private static void InvokeSync(Action<IShellEvent> handler, IShellEvent shellEvent)
    {
        try
        {
            handler(shellEvent);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Shell event handler failed for {shellEvent.GetType().Name}: {ex}");
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
