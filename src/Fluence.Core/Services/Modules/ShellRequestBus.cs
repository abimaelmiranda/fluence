using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Services.Modules;

public sealed class ShellRequestBus : IShellRequestBus
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Type, object> _handlers = new();

    public Task<TResponse?> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : IShellRequest<TResponse>
    {
        Func<TRequest, CancellationToken, Task<TResponse?>> handler;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(TRequest), out var raw))
                return Task.FromResult<TResponse?>(default);
            handler = (Func<TRequest, CancellationToken, Task<TResponse?>>)raw;
        }

        return handler(request, ct);
    }

    public IDisposable Handle<TRequest, TResponse>(Func<TRequest, CancellationToken, Task<TResponse?>> handler)
        where TRequest : IShellRequest<TResponse>
    {
        var key = typeof(TRequest);
        lock (_lock)
            _handlers[key] = handler;

        return new Registration(this, key);
    }

    private sealed class Registration(ShellRequestBus owner, Type key) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;
            lock (owner._lock)
                owner._handlers.Remove(key);
        }
    }
}
