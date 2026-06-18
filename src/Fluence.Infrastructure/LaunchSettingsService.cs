using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Storage;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;

namespace Fluence.Infrastructure;

public sealed class LaunchSettingsService(IFluenceStorageService storage) : ILaunchSettingsService
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "artifacts",
        "bin",
        "obj",
    };

    public string? GetWorkspaceRoot(Workspace workspace)
    {
        if (!string.IsNullOrWhiteSpace(workspace.CurrentSolutionPath))
            return Path.GetDirectoryName(workspace.CurrentSolutionPath);

        if (!string.IsNullOrWhiteSpace(workspace.CurrentFolderPath))
            return workspace.CurrentFolderPath;

        if (!string.IsNullOrWhiteSpace(workspace.CurrentFilePath))
            return ResolveFileWorkspaceRoot(workspace.CurrentFilePath);

        return null;
    }

    public string GetSettingsPath(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        return storage.GetProjectPath(workspaceRoot, "launch.json");
    }

    public Task<LaunchSettings?> LoadAsync(string workspaceRoot, CancellationToken cancellationToken = default) =>
        storage.ReadProjectAsync(workspaceRoot, "launch.json", LaunchSettingsJsonContext.Default.LaunchSettings, cancellationToken);

    public Task SaveAsync(string workspaceRoot, LaunchSettings settings, CancellationToken cancellationToken = default) =>
        storage.WriteProjectAsync(workspaceRoot, "launch.json", settings, LaunchSettingsJsonContext.Default.LaunchSettings, cancellationToken);

    public Task<IReadOnlyList<string>> DiscoverProjectPathsAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var projects = EnumerateProjects(workspaceRoot, cancellationToken)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult<IReadOnlyList<string>>(projects);
    }

    public async Task<IReadOnlyList<DotnetLaunchProfile>> DiscoverLaunchProfilesAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return Array.Empty<DotnetLaunchProfile>();

        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
            return Array.Empty<DotnetLaunchProfile>();

        var settingsPath = Path.Combine(projectDirectory, "Properties", "launchSettings.json");
        if (!File.Exists(settingsPath))
            return Array.Empty<DotnetLaunchProfile>();

        JsonDocument document;
        try
        {
            await using var stream = File.OpenRead(settingsPath);
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return Array.Empty<DotnetLaunchProfile>();
        }
        catch (IOException)
        {
            return Array.Empty<DotnetLaunchProfile>();
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("profiles", out var profilesElement) ||
                profilesElement.ValueKind != JsonValueKind.Object)
                return Array.Empty<DotnetLaunchProfile>();

            var profiles = new List<DotnetLaunchProfile>();
            foreach (var profileElement in profilesElement.EnumerateObject())
            {
                if (profileElement.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var commandName = GetString(profileElement.Value, "commandName");
                if (!string.Equals(commandName, "Project", StringComparison.OrdinalIgnoreCase))
                    continue;

                profiles.Add(new DotnetLaunchProfile(
                    profileElement.Name,
                    commandName ?? string.Empty,
                    GetString(profileElement.Value, "applicationUrl"),
                    ReadEnvironmentVariables(profileElement.Value),
                    GetString(profileElement.Value, "commandLineArgs"),
                    GetString(profileElement.Value, "workingDirectory")));
            }

            return profiles;
        }
    }

    private static IEnumerable<string> EnumerateProjects(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
            yield return file;

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            yield break;
        }

        foreach (var child in directories)
        {
            if (IgnoredDirectories.Contains(Path.GetFileName(child)))
                continue;

            foreach (var project in EnumerateProjects(child, cancellationToken))
                yield return project;
        }
    }

    private static string? ResolveFileWorkspaceRoot(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            try
            {
                if (Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly).Any())
                    return directory;
            }
            catch
            {
                return Path.GetDirectoryName(filePath);
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return Path.GetDirectoryName(filePath);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static IReadOnlyDictionary<string, string> ReadEnvironmentVariables(JsonElement profile)
    {
        if (!profile.TryGetProperty("environmentVariables", out var envElement) ||
            envElement.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>();

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var variable in envElement.EnumerateObject())
        {
            if (variable.Value.ValueKind == JsonValueKind.String)
                env[variable.Name] = variable.Value.GetString() ?? string.Empty;
        }

        return env;
    }
}
