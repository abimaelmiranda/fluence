namespace Fluence.Core.Workspace;

public interface ILaunchSettingsCoordinator
{
    Task<LaunchSettings?> LoadExistingAsync(CancellationToken cancellationToken = default);

    Task<LaunchSettings?> EnsureAsync(CancellationToken cancellationToken = default);
}
