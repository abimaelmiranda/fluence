using Fluence.Core.Services.Modules;

namespace Fluence.Core.Abstractions.Modules;

public interface IShutdownCoordinator
{
    bool IsShutdownComplete { get; }

    Task ShutdownAsync(
        IProgress<ModuleShutdownProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
