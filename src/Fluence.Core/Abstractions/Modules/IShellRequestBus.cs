using System;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Abstractions.Modules;

public interface IShellRequest<TResponse> { }

public interface IShellRequestBus
{
    Task<TResponse?> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : IShellRequest<TResponse>;

    IDisposable Handle<TRequest, TResponse>(Func<TRequest, CancellationToken, Task<TResponse?>> handler)
        where TRequest : IShellRequest<TResponse>;
}
