using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Services.Modules;

public sealed class ShellEventBus : IShellEventBus
{
    private readonly Lock _lock = new();
    private readonly Action<Action>? _dispatch;
    private readonly Dictionary<Type, List<Action<IShellEvent>>> _syncHandlers = new();
    private readonly Dictionary<Type, List<Func<IShellEvent, Task>>> _asyncHandlers = new();
    private readonly Dictionary<object, Action<IShellEvent>> _syncWrappers = new();
    private readonly Dictionary<object, Func<IShellEvent, Task>> _asyncWrappers = new();

    public ShellEventBus() { }

    public ShellEventBus(Action<Action> dispatch) { _dispatch = dispatch; }

    public void Publish(IShellEvent shellEvent)
    {
        var type = shellEvent.GetType();
        Action<IShellEvent>[] syncSnapshot;
        Func<IShellEvent, Task>[] asyncSnapshot;
        lock (_lock)
        {
            syncSnapshot = _syncHandlers.TryGetValue(type, out var syncHandlers)
                ? syncHandlers.ToArray()
                : [];
            asyncSnapshot = _asyncHandlers.TryGetValue(type, out var asyncHandlers)
                ? asyncHandlers.ToArray()
                : [];
        }

        foreach (var handler in syncSnapshot)
            DispatchSync(handler, shellEvent);

        foreach (var handler in asyncSnapshot)
            _ = Task.Run(() => InvokeAsync(handler, shellEvent));
    }

    public void SubscribeSync<TEvent>(Action<TEvent> handler) where TEvent : IShellEvent
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
    }

    public void SubscribeAsync<TEvent>(Func<TEvent, Task> handler) where TEvent : IShellEvent
    {
        var type = typeof(TEvent);
        Func<IShellEvent, Task> wrapper = e => handler((TEvent)e);
        lock (_lock)
        {
            if (!_asyncHandlers.TryGetValue(type, out var list))
                _asyncHandlers[type] = list = [];
            _asyncWrappers[handler] = wrapper;
            list.Add(wrapper);
        }
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

    public void UnsubscribeAsync<TEvent>(Func<TEvent, Task> handler) where TEvent : IShellEvent
    {
        var type = typeof(TEvent);
        lock (_lock)
        {
            if (!_asyncHandlers.TryGetValue(type, out var list))
                return;
            if (_asyncWrappers.TryGetValue(handler, out var wrapper))
            {
                list.Remove(wrapper);
                _asyncWrappers.Remove(handler);
            }
        }
    }

    private void DispatchSync(Action<IShellEvent> handler, IShellEvent shellEvent)
    {
        if (_dispatch is not null)
        {
            _dispatch(() => InvokeSync(handler, shellEvent));
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
            Debug.WriteLine($"Shell event sync handler failed for {shellEvent.GetType().Name}: {ex}");
        }
    }

    private static async Task InvokeAsync(Func<IShellEvent, Task> handler, IShellEvent shellEvent)
    {
        try
        {
            await handler(shellEvent).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Shell event async handler failed for {shellEvent.GetType().Name}: {ex}");
        }
    }
}
