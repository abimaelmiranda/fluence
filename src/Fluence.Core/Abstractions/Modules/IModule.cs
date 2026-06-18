using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Core.Abstractions.Modules;

public interface IModule : IAsyncDisposable
{
    string Name { get; }

    void Register(IServiceCollection services);

    void Initialize(IModuleHost host);
}
