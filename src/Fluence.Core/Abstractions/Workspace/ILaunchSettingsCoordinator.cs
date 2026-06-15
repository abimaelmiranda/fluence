using Fluence.Core.Models.Workspace;

namespace Fluence.Core.Abstractions.Workspace;

public interface ILaunchSettingsCoordinator
{
    Task<LaunchSettings?> LoadExistingAsync(CancellationToken cancellationToken = default);

    Task<LaunchSettings?> EnsureAsync(CancellationToken cancellationToken = default);
}
