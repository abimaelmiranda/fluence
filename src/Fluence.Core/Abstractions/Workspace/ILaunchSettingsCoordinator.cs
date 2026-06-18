using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Abstractions.Workspace;

public interface ILaunchSettingsCoordinator
{
    Task<LaunchSettings?> LoadExistingAsync(CancellationToken cancellationToken = default);

    Task<LaunchSettings?> EnsureAsync(
        ExecutionMode mode,
        CancellationToken cancellationToken = default);
}
