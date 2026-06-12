using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Core.Modules;

public interface IIdeModule
{
    string Name { get; }

    void Register(IServiceCollection services);

    void Initialize(IModuleHost host);
}
