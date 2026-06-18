using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.File;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Services.File;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;

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

    public async Task<LaunchSettings?> EnsureAsync(
        ExecutionMode mode,
        CancellationToken cancellationToken = default)
    {
        var workspaceRoot = settings.GetWorkspaceRoot(workspace.Current);
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return null;

        var existing = await settings.LoadAsync(workspaceRoot, cancellationToken);
        if (existing is not null)
            return await EnsureProfileAsync(workspaceRoot, existing, mode, cancellationToken);

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
        return await EnsureProfileAsync(workspaceRoot, launchSettings, mode, cancellationToken);
    }

    private async Task<LaunchSettings?> EnsureProfileAsync(
        string workspaceRoot,
        LaunchSettings launchSettings,
        ExecutionMode mode,
        CancellationToken cancellationToken)
    {
        var projectPath = ResolveProjectPath(workspaceRoot, launchSettings.StartupProject);
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
            return launchSettings;

        var profiles = await settings.DiscoverLaunchProfilesAsync(projectPath, cancellationToken);
        if (profiles.Count == 0)
            return launchSettings;

        var configuration = EnsureDefaultConfiguration(launchSettings);
        var profileName = GetProfileName(configuration, mode);
        if (!string.IsNullOrWhiteSpace(profileName) &&
            profiles.Any(profile => string.Equals(profile.Name, profileName, StringComparison.OrdinalIgnoreCase)))
            return launchSettings;

        var selectedProfile = await setupDialog.SelectLaunchProfileAsync(
            workspaceRoot,
            projectPath,
            mode,
            profiles,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(selectedProfile))
            return launchSettings;

        SetProfileName(configuration, mode, selectedProfile);
        await settings.SaveAsync(workspaceRoot, launchSettings, cancellationToken);
        return launchSettings;
    }

    private static string? ResolveProjectPath(string workspaceRoot, string startupProject)
    {
        if (string.IsNullOrWhiteSpace(startupProject))
            return null;

        return Path.IsPathRooted(startupProject)
            ? startupProject
            : Path.GetFullPath(Path.Combine(workspaceRoot, startupProject));
    }

    private static string? GetProfileName(LaunchConfiguration configuration, ExecutionMode mode) =>
        mode == ExecutionMode.Debug ? configuration.DebugProfileName : configuration.RunProfileName;

    private static LaunchConfiguration EnsureDefaultConfiguration(LaunchSettings launchSettings)
    {
        if (launchSettings.Configurations.Count == 0)
            launchSettings.Configurations.Add(new LaunchConfiguration());

        return launchSettings.Configurations[0];
    }

    private static void SetProfileName(LaunchConfiguration configuration, ExecutionMode mode, string profileName)
    {
        if (mode == ExecutionMode.Debug)
        {
            configuration.DebugProfileName = profileName;
            return;
        }

        configuration.RunProfileName = profileName;
    }
}
