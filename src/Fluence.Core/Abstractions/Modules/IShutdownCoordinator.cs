using Fluence.Core.Services.Modules;
using Fluence.Core.Models.Lifecycle;

namespace Fluence.Core.Abstractions.Modules;

public interface IShutdownCoordinator
{
    bool IsShutdownComplete { get; }

    Task ShutdownAsync(
        IProgress<ModuleShutdownProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task ShutdownAsync(
        ApplicationShutdownReason reason,
        IProgress<ModuleShutdownProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
