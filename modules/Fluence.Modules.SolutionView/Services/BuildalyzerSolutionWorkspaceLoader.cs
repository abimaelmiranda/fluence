using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Buildalyzer;
using Buildalyzer.IO;
using Fluence.Modules.SolutionView.Abstractions;
using Fluence.Modules.SolutionView.Exceptions;
using Fluence.Modules.SolutionView.Models;
using Fluence.Modules.SolutionView.Models.Enums;

namespace Fluence.Modules.SolutionView.Services;

public sealed class BuildalyzerSolutionWorkspaceLoader : ISolutionWorkspaceLoader
{
    private const int CacheFormatVersion = 2;
    private const string CacheDirectoryName = ".fluence";
    private const string CacheFilePrefix = "solution-structure";

    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly string[] SupportedItemTypes =
    [
        "Compile",
        "None",
        "Content",
        "EmbeddedResource",
        "AdditionalFiles",
        "ApplicationDefinition",
        "Page",
        "Resource",
    ];

    private static readonly HashSet<string> HiddenPathSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".hg",
        ".svn",
        ".vs",
        "bin",
        "debug",
        "obj",
        "release",
        "testresults",
    };

    public Task<SolutionWorkspaceSnapshot> LoadStructuralAsync(string solutionPath, CancellationToken cancellationToken = default)
        => Task.Run(() => LoadStructural(solutionPath, cancellationToken), cancellationToken);

    public Task<SolutionWorkspaceSnapshot> LoadAsync(string solutionPath, CancellationToken cancellationToken = default)
        => Load(solutionPath, cancellationToken);

    public bool HasValidCache(string solutionPath)
    {
        try
        {
            if (!File.Exists(solutionPath)) return false;
            var solutionInfo = SolutionFileInfo.Parse(solutionPath);
            var fingerprint = CreateCacheFingerprint(solutionPath, solutionInfo, CancellationToken.None);
            return TryLoadFromCache(solutionPath, fingerprint, out _);
        }
        catch
        {
            return false;
        }
    }

    private static SolutionWorkspaceSnapshot LoadStructural(string solutionPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(solutionPath) || !File.Exists(solutionPath))
        {
            throw new SolutionWorkspaceLoadException(solutionPath, "The solution file does not exist.");
        }

        var solutionInfo = SolutionFileInfo.Parse(solutionPath);
        var root = new MutableNode(SolutionTreeNodeKind.Solution, Path.GetFileName(solutionPath), solutionPath);

        foreach (var folderPath in solutionInfo.SolutionFolderPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var parent = root;
            foreach (var folderName in folderPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (!string.IsNullOrWhiteSpace(folderName))
                    parent = parent.GetOrAddChild(SolutionTreeNodeKind.SolutionFolder, folderName, null);
            }
        }

        foreach (var projectPath in solutionInfo.ProjectPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(projectPath) || IsHiddenPath(projectPath))
                continue;

            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var projectNode = new MutableNode(SolutionTreeNodeKind.Project, projectName, projectPath);
            var folderPath = solutionInfo.GetFolderPath(projectPath);

            var parent = root;
            foreach (var folder in folderPath)
                parent = parent.GetOrAddChild(SolutionTreeNodeKind.SolutionFolder, folder, null);

            parent.Children.Add(projectNode);
        }

        return new SolutionWorkspaceSnapshot(solutionPath, root.ToImmutable());
    }

    private static async Task<SolutionWorkspaceSnapshot> Load(string solutionPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(solutionPath) || !File.Exists(solutionPath))
        {
            throw new SolutionWorkspaceLoadException(solutionPath, "The solution file does not exist.");
        }

        var solutionInfo = SolutionFileInfo.Parse(solutionPath);
        var fingerprint = CreateCacheFingerprint(solutionPath, solutionInfo, cancellationToken);
        if (TryLoadFromCache(solutionPath, fingerprint, out var cachedSnapshot))
        {
            return cachedSnapshot;
        }

        var projects = await LoadProjectsWithBuildalyzerAsync(solutionPath, solutionInfo, cancellationToken);

        var rootBuilder = new MutableNode(SolutionTreeNodeKind.Solution, Path.GetFileName(solutionPath), solutionPath);
        foreach (var folderPath in solutionInfo.SolutionFolderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var parent = rootBuilder;
            foreach (var folderName in folderPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    continue;
                }

                parent = parent.GetOrAddChild(SolutionTreeNodeKind.SolutionFolder, folderName, null);
            }
        }

        foreach (var project in projects.OrderBy(project => string.Join("/", project.SolutionFolderPath), StringComparer.OrdinalIgnoreCase)
                                       .ThenBy(project => project.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parent = rootBuilder;
            foreach (var folderName in project.SolutionFolderPath)
            {
                parent = parent.GetOrAddChild(SolutionTreeNodeKind.SolutionFolder, folderName, null);
            }

            parent.Children.Add(project.Node);
        }

        var snapshot = new SolutionWorkspaceSnapshot(solutionPath, rootBuilder.ToImmutable());
        SaveToCache(solutionPath, fingerprint, snapshot);
        return snapshot;
    }

    private static bool TryLoadFromCache(
        string solutionPath,
        SolutionCacheFingerprint fingerprint,
        out SolutionWorkspaceSnapshot snapshot)
    {
        snapshot = null!;

        try
        {
            var cachePath = GetCacheFilePath(solutionPath);
            if (!File.Exists(cachePath))
            {
                return false;
            }

            using var stream = File.OpenRead(cachePath);
            var cache = JsonSerializer.Deserialize<SolutionStructureCache>(stream, CacheJsonOptions);
            if (cache is null ||
                cache.FormatVersion != CacheFormatVersion ||
                cache.Fingerprint is null ||
                cache.Root is null ||
                !string.Equals(cache.SolutionPath, solutionPath, StringComparison.OrdinalIgnoreCase) ||
                !HasSameFingerprint(fingerprint, cache.Fingerprint))
            {
                return false;
            }

            snapshot = new SolutionWorkspaceSnapshot(solutionPath, cache.Root);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveToCache(
        string solutionPath,
        SolutionCacheFingerprint fingerprint,
        SolutionWorkspaceSnapshot snapshot)
    {
        try
        {
            var cacheDirectory = GetCacheDirectoryPath(solutionPath);
            Directory.CreateDirectory(cacheDirectory);

            var cache = new SolutionStructureCache(
                CacheFormatVersion,
                solutionPath,
                DateTimeOffset.UtcNow,
                fingerprint,
                snapshot.Root);

            var cachePath = GetCacheFilePath(solutionPath);
            var temporaryPath = $"{cachePath}.tmp";
            using (var stream = File.Create(temporaryPath))
            {
                JsonSerializer.Serialize(stream, cache, CacheJsonOptions);
            }

            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        catch
        {
        }
    }

    private static string GetCacheDirectoryPath(string solutionPath)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
        return Path.Combine(solutionDirectory, CacheDirectoryName);
    }

    private static string GetCacheFilePath(string solutionPath)
    {
        var solutionName = Path.GetFileNameWithoutExtension(solutionPath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(solutionPath))))
            .ToLowerInvariant()[..12];
        return Path.Combine(GetCacheDirectoryPath(solutionPath), $"{CacheFilePrefix}-{solutionName}-{hash}.json");
    }

    private static SolutionCacheFingerprint CreateCacheFingerprint(
        string solutionPath,
        SolutionFileInfo solutionInfo,
        CancellationToken cancellationToken)
    {
        var files = solutionInfo.ProjectPaths
            .Append(Path.GetFullPath(solutionPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(CreateCacheFileFingerprint)
            .ToArray();

        var visibleProjectFiles = solutionInfo.ProjectPaths
            .SelectMany(path => GetVisibleProjectFilePaths(path, cancellationToken))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var visibleProjectDirectories = solutionInfo.ProjectPaths
            .SelectMany(path => GetProjectDirectories(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SolutionCacheFingerprint(files, visibleProjectFiles, visibleProjectDirectories);
    }

    private static SolutionCacheFileFingerprint CreateCacheFileFingerprint(string path)
    {
        if (!File.Exists(path))
        {
            return new SolutionCacheFileFingerprint(Path.GetFullPath(path), null, null);
        }

        var info = new FileInfo(path);
        return new SolutionCacheFileFingerprint(
            Path.GetFullPath(path),
            info.LastWriteTimeUtc.Ticks,
            info.Length);
    }

    private static IEnumerable<string> GetVisibleProjectFilePaths(string projectPath, CancellationToken cancellationToken)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories)
                .Where(path =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return IsVisibleProjectCacheFile(path);
                })
                .Select(Path.GetFullPath)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsVisibleProjectCacheFile(string path)
    {
        return !IsHiddenPath(path) && !HasHiddenAttributes(path);
    }

    private static bool HasSameFingerprint(
        SolutionCacheFingerprint current,
        SolutionCacheFingerprint cached)
    {
        if (current.Files.Count != cached.Files.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Files.Count; index++)
        {
            var currentFile = current.Files[index];
            var cachedFile = cached.Files[index];
            if (!string.Equals(currentFile.Path, cachedFile.Path, StringComparison.OrdinalIgnoreCase) ||
                currentFile.LastWriteTimeUtcTicks != cachedFile.LastWriteTimeUtcTicks ||
                currentFile.Length != cachedFile.Length)
            {
                return false;
            }
        }

        if (!HasSamePaths(current.VisibleProjectFiles, cached.VisibleProjectFiles))
        {
            return false;
        }

        return HasSamePaths(current.VisibleProjectDirectories, cached.VisibleProjectDirectories);
    }

    private static bool HasSamePaths(
        IReadOnlyList<string> current,
        IReadOnlyList<string> cached)
    {
        if (current.Count != cached.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            if (!string.Equals(current[index], cached[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<List<ProjectLoadResult>> LoadProjectsWithBuildalyzerAsync(
        string solutionPath,
        SolutionFileInfo solutionInfo,
        CancellationToken cancellationToken)
    {
        AnalyzerManager manager;
        try
        {
            manager = solutionInfo.ProjectPaths.Count > 0
                ? new AnalyzerManager()
                : new AnalyzerManager(IOPath.Empty.Combine(solutionPath));
        }
        catch (Exception ex)
        {
            throw new SolutionWorkspaceLoadException(solutionPath, "Buildalyzer could not open this solution.", ex);
        }

        var projects = GetProjects(manager, solutionInfo);
        var solutionProjectPaths = projects
            .Select(project => project.ProjectPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new ConcurrentBag<ProjectLoadResult>();

        await Parallel.ForEachAsync(
            projects,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
            async (project, ct) =>
            {
                var (projectPath, analyzer) = project;

                if (!File.Exists(projectPath) || IsHiddenPath(projectPath))
                    return;

                var projectName = Path.GetFileNameWithoutExtension(projectPath);
                var projectBuilder = new MutableNode(SolutionTreeNodeKind.Project, projectName, projectPath);
                AddDependencies(projectBuilder, projectPath, solutionProjectPaths);
                projectBuilder.Children.Add(new MutableNode(SolutionTreeNodeKind.File, Path.GetFileName(projectPath), projectPath));

                foreach (var directory in GetProjectDirectories(projectPath))
                {
                    ct.ThrowIfCancellationRequested();
                    AddProjectDirectory(projectBuilder, projectPath, directory);
                }

                var files = await Task.Run(() => GetProjectFiles(projectPath, analyzer), ct);
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    AddProjectFile(projectBuilder, projectPath, file);
                }

                var folderPath = solutionInfo.GetFolderPath(projectPath);
                results.Add(new ProjectLoadResult(projectName, folderPath, projectBuilder));
            });

        return results.ToList();
    }

    private static void AddDependencies(
        MutableNode projectBuilder,
        string projectPath,
        IReadOnlySet<string> solutionProjectPaths)
    {
        var dependencies = new MutableNode(SolutionTreeNodeKind.Dependencies, "Dependencies", null);
        var projectReferences = new MutableNode(SolutionTreeNodeKind.DependencyGroup, "Projects", null);
        var packageReferences = new MutableNode(SolutionTreeNodeKind.DependencyGroup, "Packages", null);

        foreach (var reference in ReadProjectReferences(projectPath))
        {
            var referencedPath = ResolveProjectReferencePath(projectPath, reference.Include);
            var resolvedPath = referencedPath is not null &&
                               solutionProjectPaths.Contains(referencedPath) &&
                               File.Exists(referencedPath)
                ? referencedPath
                : null;
            var displayName = resolvedPath is not null
                ? Path.GetFileNameWithoutExtension(resolvedPath)
                : reference.Include;

            projectReferences.Children.Add(new MutableNode(
                SolutionTreeNodeKind.ProjectReference,
                displayName,
                referencedPath,
                projectPath: projectPath,
                referencedProjectPath: referencedPath,
                isResolved: resolvedPath is not null));
        }

        foreach (var package in ReadPackageReferences(projectPath))
        {
            var displayName = string.IsNullOrWhiteSpace(package.Version)
                ? package.Id
                : $"{package.Id} ({package.Version})";

            packageReferences.Children.Add(new MutableNode(
                SolutionTreeNodeKind.PackageReference,
                displayName,
                null,
                version: package.Version,
                projectPath: projectPath));
        }

        dependencies.Children.Add(projectReferences);
        dependencies.Children.Add(packageReferences);
        projectBuilder.Children.Add(dependencies);
    }

    private static IReadOnlyList<ProjectReferenceInfo> ReadProjectReferences(string projectPath)
    {
        try
        {
            var document = XDocument.Load(projectPath);
            return document.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.OrdinalIgnoreCase))
                .Select(element => (string?)element.Attribute("Include"))
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => new ProjectReferenceInfo(include!))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<PackageReferenceInfo> ReadPackageReferences(string projectPath)
    {
        try
        {
            var document = XDocument.Load(projectPath);
            return document.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "PackageReference", StringComparison.OrdinalIgnoreCase))
                .Select(element =>
                {
                    var id = (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update");
                    var version = (string?)element.Attribute("Version") ??
                                  element.Elements().FirstOrDefault(child => string.Equals(child.Name.LocalName, "Version", StringComparison.OrdinalIgnoreCase))?.Value;
                    return string.IsNullOrWhiteSpace(id) ? null : new PackageReferenceInfo(id, version);
                })
                .Where(package => package is not null)
                .Select(package => package!)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static string? ResolveProjectReferencePath(string projectPath, string include)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return null;
        }

        var normalizedInclude = NormalizeProjectInclude(include);
        return Path.GetFullPath(Path.IsPathRooted(normalizedInclude)
            ? normalizedInclude
            : Path.Combine(projectDirectory, normalizedInclude));
    }

    private static string NormalizeProjectInclude(string include)
        => include.Replace('\\', Path.DirectorySeparatorChar)
                  .Replace('/', Path.DirectorySeparatorChar);

    private static List<(string ProjectPath, IProjectAnalyzer Analyzer)> GetProjects(AnalyzerManager manager, SolutionFileInfo solutionInfo)
    {
        var projects = new List<(string ProjectPath, IProjectAnalyzer Analyzer)>();
        foreach (var entry in manager.Projects)
        {
            projects.Add((Path.GetFullPath(entry.Key), entry.Value));
        }

        foreach (var projectPath in solutionInfo.ProjectPaths)
        {
            if (projects.Any(project => string.Equals(project.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var analyzer = manager.GetProject(IOPath.Empty.Combine(projectPath));
            if (analyzer is not null)
            {
                projects.Add((projectPath, analyzer));
            }
        }

        return projects;
    }

    private static IReadOnlySet<string> GetProjectFiles(string projectPath, IProjectAnalyzer analyzer)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        object? result = null;
        try
        {
            result = analyzer.Build().FirstOrDefault();
        }
        catch
        {
            return files;
        }

        if (result is null)
        {
            return files;
        }

        AddSourceFiles(files, result);
        AddMsBuildItems(files, projectPath, result);
        return files;
    }

    private static void AddSourceFiles(HashSet<string> files, object result)
    {
        if (result.GetType().GetProperty("SourceFiles")?.GetValue(result) is not IEnumerable sourceFiles)
        {
            return;
        }

        foreach (var sourceFile in sourceFiles)
        {
            if (sourceFile is string path && IsVisibleFile(path))
            {
                files.Add(Path.GetFullPath(path));
            }
        }
    }

    private static void AddMsBuildItems(HashSet<string> files, string projectPath, object result)
    {
        if (result.GetType().GetProperty("Items")?.GetValue(result) is not object items)
        {
            return;
        }

        var projectDirectory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
        foreach (var itemType in SupportedItemTypes)
        {
            if (!TryGetDictionaryValue(items, itemType, out var projectItems) || projectItems is not IEnumerable enumerableItems)
            {
                continue;
            }

            foreach (var item in enumerableItems)
            {
                var path = GetItemPath(item, projectDirectory);
                if (path is not null && IsVisibleFile(path))
                {
                    files.Add(Path.GetFullPath(path));
                }
            }
        }
    }

    private static bool TryGetDictionaryValue(object dictionary, string key, out object? value)
    {
        value = null;
        var tryGetValueMethod = dictionary.GetType().GetMethod("TryGetValue");
        if (tryGetValueMethod is null)
        {
            return false;
        }

        var parameters = new object?[] { key, null };
        var found = tryGetValueMethod.Invoke(dictionary, parameters) as bool? == true;
        value = parameters[1];
        return found;
    }

    private static string? GetItemPath(object? item, string projectDirectory)
    {
        if (item is null)
        {
            return null;
        }

        var itemType = item.GetType();
        var metadata = itemType.GetProperty("Metadata")?.GetValue(item);
        if (metadata is not null && TryGetDictionaryValue(metadata, "FullPath", out var fullPath) && fullPath is string fullPathValue)
        {
            return fullPathValue;
        }

        var itemSpec = itemType.GetProperty("ItemSpec")?.GetValue(item) as string;
        if (string.IsNullOrWhiteSpace(itemSpec))
        {
            return null;
        }

        return Path.IsPathRooted(itemSpec) ? itemSpec : Path.Combine(projectDirectory, itemSpec);
    }

    private static void AddProjectFile(MutableNode projectBuilder, string projectPath, string filePath)
    {
        if (string.Equals(projectPath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return;
        }

        var relativePath = Path.GetRelativePath(projectDirectory, filePath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            projectBuilder.Children.Add(new MutableNode(SolutionTreeNodeKind.File, Path.GetFileName(filePath), filePath));
            return;
        }

        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Any(part => string.IsNullOrWhiteSpace(part) || HiddenPathSegments.Contains(part)))
        {
            return;
        }

        var parent = projectBuilder;
        var currentDirectory = projectDirectory;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            currentDirectory = Path.Combine(currentDirectory, parts[index]);
            parent = parent.GetOrAddChild(SolutionTreeNodeKind.Folder, parts[index], currentDirectory, projectPath);
        }

        parent.GetOrAddChild(SolutionTreeNodeKind.File, parts[^1], filePath, projectPath);
    }

    private static void AddProjectDirectory(MutableNode projectBuilder, string projectPath, string directoryPath)
    {
        if (!IsVisibleDirectory(directoryPath))
        {
            return;
        }

        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return;
        }

        var relativePath = Path.GetRelativePath(projectDirectory, directoryPath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            return;
        }

        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Any(part => string.IsNullOrWhiteSpace(part) || HiddenPathSegments.Contains(part)))
        {
            return;
        }

        var parent = projectBuilder;
        var currentDirectory = projectDirectory;
        for (var index = 0; index < parts.Length; index++)
        {
            currentDirectory = Path.Combine(currentDirectory, parts[index]);
            parent = parent.GetOrAddChild(SolutionTreeNodeKind.Folder, parts[index], currentDirectory, projectPath);
        }
    }

    private static bool IsVisibleFile(string path)
    {
        return File.Exists(path) && !IsHiddenPath(path) && !HasHiddenAttributes(path);
    }

    private static IEnumerable<string> GetProjectDirectories(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(projectDirectory, "*", SearchOption.AllDirectories)
                .Where(path => IsVisibleDirectory(path))
                .Select(Path.GetFullPath)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsVisibleDirectory(string path)
    {
        return Directory.Exists(path) && !IsHiddenPath(path) && !HasHiddenAttributes(path);
    }

    private static bool IsHiddenPath(string path)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   .Any(segment => HiddenPathSegments.Contains(segment) ||
                                   (segment.Length > 1 && segment.StartsWith(".", StringComparison.Ordinal)));
    }

    private static bool HasHiddenAttributes(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Hidden) == FileAttributes.Hidden;
        }
        catch
        {
            return true;
        }
    }

    private sealed record ProjectLoadResult(
        string Name,
        IReadOnlyList<string> SolutionFolderPath,
        MutableNode Node);

    private sealed record ProjectReferenceInfo(string Include);

    private sealed record PackageReferenceInfo(string Id, string? Version);

    private sealed record SolutionStructureCache(
        int FormatVersion,
        string SolutionPath,
        DateTimeOffset CreatedAtUtc,
        SolutionCacheFingerprint Fingerprint,
        SolutionTreeNode Root);

    private sealed record SolutionCacheFingerprint(
        IReadOnlyList<SolutionCacheFileFingerprint> Files,
        IReadOnlyList<string> VisibleProjectFiles,
        IReadOnlyList<string> VisibleProjectDirectories);

    private sealed record SolutionCacheFileFingerprint(
        string Path,
        long? LastWriteTimeUtcTicks,
        long? Length);

    private sealed class MutableNode(
        SolutionTreeNodeKind kind,
        string name,
        string? path,
        string? version = null,
        string? projectPath = null,
        string? referencedProjectPath = null,
        bool isResolved = true)
    {
        public SolutionTreeNodeKind Kind { get; } = kind;
        public string Name { get; } = name;
        public string? Path { get; } = path;
        public string? Version { get; } = version;
        public string? ProjectPath { get; } = projectPath;
        public string? ReferencedProjectPath { get; } = referencedProjectPath;
        public bool IsResolved { get; } = isResolved;
        public List<MutableNode> Children { get; } = [];

        public MutableNode GetOrAddChild(SolutionTreeNodeKind kind, string name, string? path, string? projectPath = null)
        {
            var existing = Children.FirstOrDefault(child =>
                child.Kind == kind &&
                string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(child.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }

            var child = new MutableNode(kind, name, path, projectPath: projectPath);
            Children.Add(child);
            return child;
        }

        public SolutionTreeNode ToImmutable()
        {
            var orderedChildren = Children
                .OrderBy(child => GetSortOrder(child.Kind))
                .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
                .Select(child => child.ToImmutable())
                .ToArray();
            return new SolutionTreeNode(Kind, Name, Path, orderedChildren, Version, ProjectPath, ReferencedProjectPath, IsResolved);
        }

        private static int GetSortOrder(SolutionTreeNodeKind kind)
        {
            return kind switch
            {
                SolutionTreeNodeKind.Dependencies => 0,
                SolutionTreeNodeKind.DependencyGroup => 1,
                SolutionTreeNodeKind.ProjectReference => 2,
                SolutionTreeNodeKind.PackageReference => 3,
                SolutionTreeNodeKind.SolutionFolder => 4,
                SolutionTreeNodeKind.Project => 5,
                SolutionTreeNodeKind.Folder => 6,
                SolutionTreeNodeKind.File => 7,
                _ => 9,
            };
        }
    }

    private sealed class SolutionFileInfo
    {
        private static readonly Regex ProjectLinePattern = new(
            "^Project\\(\"(?<type>[^\"]+)\"\\) = \"(?<name>[^\"]+)\", \"(?<path>[^\"]+)\", \"(?<id>[^\"]+)\"",
            RegexOptions.Compiled);

        private const string SolutionFolderTypeGuid = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";

        private readonly Dictionary<string, string> _projectIdByPath;
        private readonly Dictionary<string, string> _folderNameById;
        private readonly Dictionary<string, string> _parentIdById;
        private readonly Dictionary<string, IReadOnlyList<string>> _slnxFolderPathByProjectPath;

        private SolutionFileInfo(
            string solutionDirectory,
            Dictionary<string, string> projectIdByPath,
            Dictionary<string, string> folderNameById,
            Dictionary<string, string> parentIdById,
            Dictionary<string, IReadOnlyList<string>> slnxFolderPathByProjectPath)
        {
            SolutionDirectory = solutionDirectory;
            _projectIdByPath = projectIdByPath;
            _folderNameById = folderNameById;
            _parentIdById = parentIdById;
            _slnxFolderPathByProjectPath = slnxFolderPathByProjectPath;
        }

        public string SolutionDirectory { get; }

        public IReadOnlyList<string> ProjectPaths { get; private init; } = [];

        public IReadOnlyList<string> SolutionFolderPaths { get; private init; } = [];

        public static SolutionFileInfo Parse(string solutionPath)
        {
            var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
            var projectIdByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var folderNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parentIdById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var slnxFolderPathByProjectPath = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

            if (!string.Equals(Path.GetExtension(solutionPath), ".sln", StringComparison.OrdinalIgnoreCase))
            {
                var slnxSolutionFolderPaths = new List<string>();
                var slnxProjectPaths = ParseSlnx(solutionPath, solutionDirectory, slnxFolderPathByProjectPath, slnxSolutionFolderPaths);
                return new SolutionFileInfo(
                    solutionDirectory,
                    projectIdByPath,
                    folderNameById,
                    parentIdById,
                    slnxFolderPathByProjectPath)
                {
                    ProjectPaths = slnxProjectPaths,
                    SolutionFolderPaths = slnxSolutionFolderPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                };
            }

            var inNestedProjects = false;
            foreach (var rawLine in File.ReadLines(solutionPath))
            {
                var line = rawLine.Trim();
                var match = ProjectLinePattern.Match(line);
                if (match.Success)
                {
                    var id = match.Groups["id"].Value;
                    var type = match.Groups["type"].Value;
                    var name = match.Groups["name"].Value;
                    var relativePath = match.Groups["path"].Value;

                    if (string.Equals(type, SolutionFolderTypeGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        folderNameById[id] = name;
                    }
                    else if (relativePath.EndsWith("proj", StringComparison.OrdinalIgnoreCase))
                    {
                        projectIdByPath[Path.GetFullPath(Path.Combine(solutionDirectory, relativePath))] = id;
                    }

                    continue;
                }

                if (line.StartsWith("GlobalSection(NestedProjects)", StringComparison.Ordinal))
                {
                    inNestedProjects = true;
                    continue;
                }

                if (inNestedProjects && line.StartsWith("EndGlobalSection", StringComparison.Ordinal))
                {
                    inNestedProjects = false;
                    continue;
                }

                if (!inNestedProjects)
                {
                    continue;
                }

                var parts = line.Split('=', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    parentIdById[parts[0]] = parts[1];
                }
            }

            return new SolutionFileInfo(
                solutionDirectory,
                projectIdByPath,
                folderNameById,
                parentIdById,
                slnxFolderPathByProjectPath)
            {
                ProjectPaths = projectIdByPath.Keys.ToArray(),
                SolutionFolderPaths = ParseSlnFolderPaths(solutionPath, folderNameById, parentIdById),
            };
        }

        public IReadOnlyList<string> GetFolderPath(string projectPath)
        {
            if (_slnxFolderPathByProjectPath.TryGetValue(Path.GetFullPath(projectPath), out var slnxFolderPath))
            {
                return slnxFolderPath;
            }

            if (!_projectIdByPath.TryGetValue(Path.GetFullPath(projectPath), out var id))
            {
                return [];
            }

            var folders = new Stack<string>();
            while (_parentIdById.TryGetValue(id, out var parentId))
            {
                if (_folderNameById.TryGetValue(parentId, out var folderName))
                {
                    foreach (var segment in SplitSlnxFolderName(folderName).Reverse())
                    {
                        folders.Push(segment);
                    }
                }

                id = parentId;
            }

            return folders.ToArray();
        }

        private static IReadOnlyList<string> ParseSlnx(
            string solutionPath,
            string solutionDirectory,
            Dictionary<string, IReadOnlyList<string>> folderPathByProjectPath)
        {
            return ParseSlnx(solutionPath, solutionDirectory, folderPathByProjectPath, new List<string>());
        }

        private static IReadOnlyList<string> ParseSlnx(
            string solutionPath,
            string solutionDirectory,
            Dictionary<string, IReadOnlyList<string>> folderPathByProjectPath,
            List<string> solutionFolderPaths)
        {
            var document = XDocument.Load(solutionPath);
            var projects = new List<string>();
            var root = document.Root;
            if (root is null)
            {
                return projects;
            }

            ReadSlnxElement(root, solutionDirectory, Array.Empty<string>(), folderPathByProjectPath, solutionFolderPaths, projects);
            return projects;
        }

        private static void ReadSlnxElement(
            XElement element,
            string solutionDirectory,
            IReadOnlyList<string> folderPath,
            Dictionary<string, IReadOnlyList<string>> folderPathByProjectPath,
            List<string> solutionFolderPaths,
            List<string> projects)
        {
            foreach (var child in element.Elements())
            {
                if (string.Equals(child.Name.LocalName, "Folder", StringComparison.OrdinalIgnoreCase))
                {
                    var folderName = (string?)child.Attribute("Name") ?? string.Empty;
                    var childFolderPath = folderPath.Concat(SplitSlnxFolderName(folderName)).ToArray();
                    if (childFolderPath.Length > 0)
                    {
                        solutionFolderPaths.Add(string.Join("/", childFolderPath));
                    }
                    ReadSlnxElement(child, solutionDirectory, childFolderPath, folderPathByProjectPath, solutionFolderPaths, projects);
                    continue;
                }

                if (!string.Equals(child.Name.LocalName, "Project", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var path = (string?)child.Attribute("Path");
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var projectPath = Path.GetFullPath(Path.Combine(solutionDirectory, path));
                projects.Add(projectPath);
                folderPathByProjectPath[projectPath] = folderPath.ToArray();
            }
        }

        private static IReadOnlyList<string> ParseSlnFolderPaths(
            string solutionPath,
            Dictionary<string, string> folderNameById,
            Dictionary<string, string> parentIdById)
        {
            var folderPaths = new List<string>();
            foreach (var id in folderNameById.Keys)
            {
                var path = GetFolderPath(id, folderNameById, parentIdById);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    folderPaths.Add(path);
                }
            }

            return folderPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string GetFolderPath(
            string folderId,
            IReadOnlyDictionary<string, string> folderNameById,
            IReadOnlyDictionary<string, string> parentIdById)
        {
            var folders = new Stack<string>();
            var currentId = folderId;

            while (true)
            {
                if (folderNameById.TryGetValue(currentId, out var folderName))
                {
                    foreach (var segment in SplitSlnxFolderName(folderName).Reverse())
                    {
                        folders.Push(segment);
                    }
                }

                if (!parentIdById.TryGetValue(currentId, out var parentId))
                {
                    break;
                }

                currentId = parentId;
            }

            return string.Join("/", folders);
        }

        private static IReadOnlyList<string> SplitSlnxFolderName(string folderName)
        {
            return folderName
                .Split(new[] { '/', '\\' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();
        }
    }
}
