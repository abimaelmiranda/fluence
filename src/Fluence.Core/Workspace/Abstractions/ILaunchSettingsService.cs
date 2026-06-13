namespace Fluence.Core.Workspace;

public interface ILaunchSettingsService
{
    string? GetWorkspaceRoot(Workspace workspace);

    string GetSettingsPath(string workspaceRoot);

    Task<LaunchSettings?> LoadAsync(string workspaceRoot, CancellationToken cancellationToken = default);

    Task SaveAsync(string workspaceRoot, LaunchSettings settings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> DiscoverProjectPathsAsync(string workspaceRoot, CancellationToken cancellationToken = default);
}
