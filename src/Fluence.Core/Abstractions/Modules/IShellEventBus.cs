using System;

namespace Fluence.Core.Abstractions.Modules;

public interface IShellEvent { }

public interface IShellEventBus
{
    void Publish(IShellEvent shellEvent);
    IDisposable SubscribeSync<TEvent>(Action<TEvent> handler) where TEvent : IShellEvent;
    void UnsubscribeSync<TEvent>(Action<TEvent> handler) where TEvent : IShellEvent;
}
