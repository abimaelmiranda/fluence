using Microsoft.Extensions.DependencyInjection;
using Fluence.Core.Models.Modules;

namespace Fluence.Core.Abstractions.Modules;

public interface IModule : IAsyncDisposable
{
    string Id { get; }

    string DisplayName { get; }

    int StartupOrder { get; }

    void Register(IServiceCollection services);

    ModuleContributions GetContributions() => ModuleContributions.Empty;

    Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken);
}
