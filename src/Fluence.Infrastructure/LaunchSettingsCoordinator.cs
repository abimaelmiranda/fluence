using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Ports;
using Fluence.Core.Workspace;

namespace Fluence.Infrastructure;

public sealed class LaunchSettingsCoordinator(
    IWorkspaceContext workspace,
    ILaunchSettingsService settings,
    ILaunchSetupDialogService setupDialog)
    : ILaunchSettingsCoordinator
{
    public Task<LaunchSettings?> LoadExistingAsync(CancellationToken cancellationToken = default)
    {
        var workspaceRoot = settings.GetWorkspaceRoot(workspace.Current);
        return string.IsNullOrWhiteSpace(workspaceRoot)
            ? Task.FromResult<LaunchSettings?>(null)
            : settings.LoadAsync(workspaceRoot, cancellationToken);
    }

    public async Task<LaunchSettings?> EnsureAsync(CancellationToken cancellationToken = default)
    {
        var workspaceRoot = settings.GetWorkspaceRoot(workspace.Current);
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return null;

        var existing = await settings.LoadAsync(workspaceRoot, cancellationToken);
        if (existing is not null)
            return existing;

        var projects = await settings.DiscoverProjectPathsAsync(workspaceRoot, cancellationToken);
        if (projects.Count == 0)
            return null;

        var selectedProject = await setupDialog.SelectStartupProjectAsync(workspaceRoot, projects, cancellationToken);
        if (string.IsNullOrWhiteSpace(selectedProject))
            return null;

        var launchSettings = new LaunchSettings
        {
            StartupProject = Path.GetRelativePath(workspaceRoot, selectedProject),
            Configurations =
            [
                new LaunchConfiguration
                {
                    Name = "Default",
                    Architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                },
            ],
        };

        await settings.SaveAsync(workspaceRoot, launchSettings, cancellationToken);
        return launchSettings;
    }
}
