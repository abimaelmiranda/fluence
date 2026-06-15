using System;
using System.Collections.Generic;
using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Services.Modules;

public sealed class ShellEventBus : IShellEventBus
{
    private readonly Dictionary<Type, List<Action<IShellEvent>>> _handlers = new();
    private readonly Dictionary<object, Action<IShellEvent>> _wrappers = new();

    public void Publish(IShellEvent shellEvent)
    {
        var type = shellEvent.GetType();
        if (!_handlers.TryGetValue(type, out var handlers))
            return;

        foreach (var h in handlers.ToArray())
            h(shellEvent);
    }

    public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IShellEvent
    {
        var type = typeof(TEvent);
        if (!_handlers.TryGetValue(type, out var list))
            _handlers[type] = list = [];

        Action<IShellEvent> wrapper = e => handler((TEvent)e);
        _wrappers[handler] = wrapper;
        list.Add(wrapper);
    }

    public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : IShellEvent
    {
        var type = typeof(TEvent);
        if (!_handlers.TryGetValue(type, out var list))
            return;

        if (_wrappers.TryGetValue(handler, out var wrapper))
        {
            list.Remove(wrapper);
            _wrappers.Remove(handler);
        }
    }
}
