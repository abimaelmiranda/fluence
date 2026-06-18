using Fluence.Core.Models.Workspace;
using LaunchSettings = Fluence.Core.Models.Workspace.LaunchSettings;
using WorkspaceEntity = Fluence.Core.Models.Workspace.Workspace;

namespace Fluence.Core.Abstractions.Workspace;

public interface ILaunchSettingsService
{
    string? GetWorkspaceRoot(WorkspaceEntity workspace);

    string GetSettingsPath(string workspaceRoot);

    Task<LaunchSettings?> LoadAsync(string workspaceRoot, CancellationToken cancellationToken = default);

    Task SaveAsync(string workspaceRoot, LaunchSettings settings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> DiscoverProjectPathsAsync(string workspaceRoot, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DotnetLaunchProfile>> DiscoverLaunchProfilesAsync(
        string projectPath,
        CancellationToken cancellationToken = default);
}
