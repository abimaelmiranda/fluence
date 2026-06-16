using System;
using System.Collections.Generic;
using System.Threading;
using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Services.Modules;

public sealed class ShellEventBus : IShellEventBus
{
    private readonly Lock _lock = new();
    private readonly Action<Action>? _dispatch;
    private readonly Dictionary<Type, List<Action<IShellEvent>>> _handlers = new();
    private readonly Dictionary<object, Action<IShellEvent>> _wrappers = new();

    public ShellEventBus() { }

    public ShellEventBus(Action<Action> dispatch) { _dispatch = dispatch; }

    public void Publish(IShellEvent shellEvent)
    {
        var type = shellEvent.GetType();
        Action<IShellEvent>[]? snapshot;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(type, out var handlers))
                return;
            snapshot = handlers.ToArray();
        }

        if (_dispatch is not null)
            foreach (var h in snapshot)
                _dispatch(() => h(shellEvent));
        else
            foreach (var h in snapshot)
                h(shellEvent);
    }

    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IShellEvent
    {
        var type = typeof(TEvent);
        Action<IShellEvent> wrapper = e => handler((TEvent)e);
        lock (_lock)
        {
            if (!_handlers.TryGetValue(type, out var list))
                _handlers[type] = list = [];
            _wrappers[handler] = wrapper;
            list.Add(wrapper);
        }
    }

    public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : IShellEvent
    {
        var type = typeof(TEvent);
        lock (_lock)
        {
            if (!_handlers.TryGetValue(type, out var list))
                return;
            if (_wrappers.TryGetValue(handler, out var wrapper))
            {
                list.Remove(wrapper);
                _wrappers.Remove(handler);
            }
        }
    }
}
