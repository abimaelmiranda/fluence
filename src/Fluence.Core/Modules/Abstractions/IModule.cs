using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Core.Modules.Abstractions;

public interface IModule
{
    string Name { get; }

    void Register(IServiceCollection services);

    void Initialize(IModuleHost host);
}