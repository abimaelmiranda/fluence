using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Workspace;

namespace Fluence.Infrastructure;

public sealed class LaunchSettingsService : ILaunchSettingsService
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
        return Path.Combine(workspaceRoot, ".fluence", "launch.json");
    }

    public async Task<LaunchSettings?> LoadAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var settingsPath = GetSettingsPath(workspaceRoot);
        if (!File.Exists(settingsPath))
            return null;

        await using var stream = File.OpenRead(settingsPath);
        return await System.Text.Json.JsonSerializer.DeserializeAsync(
            stream,
            LaunchSettingsJsonContext.Default.LaunchSettings,
            cancellationToken);
    }

    public async Task SaveAsync(string workspaceRoot, LaunchSettings settings, CancellationToken cancellationToken = default)
    {
        var settingsPath = GetSettingsPath(workspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

        await using var stream = File.Create(settingsPath);
        await System.Text.Json.JsonSerializer.SerializeAsync(
            stream,
            settings,
            LaunchSettingsJsonContext.Default.LaunchSettings,
            cancellationToken);
    }

    public Task<IReadOnlyList<string>> DiscoverProjectPathsAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var projects = EnumerateProjects(workspaceRoot, cancellationToken)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult<IReadOnlyList<string>>(projects);
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
}
