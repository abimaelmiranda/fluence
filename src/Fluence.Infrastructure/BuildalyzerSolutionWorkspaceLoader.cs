using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Buildalyzer;
using Fluence.Application.Workspace;

namespace Fluence.Infrastructure;

public sealed class BuildalyzerSolutionWorkspaceLoader : ISolutionWorkspaceLoader
{
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

    public Task<SolutionWorkspaceSnapshot> LoadAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Load(solutionPath, cancellationToken), cancellationToken);
    }

    private static SolutionWorkspaceSnapshot Load(string solutionPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(solutionPath) || !File.Exists(solutionPath))
        {
            throw new SolutionWorkspaceLoadException(solutionPath, "The solution file does not exist.");
        }

        var solutionInfo = SolutionFileInfo.Parse(solutionPath);
        var projects = LoadProjectsWithBuildalyzer(solutionPath, solutionInfo, cancellationToken);
        if (projects.Count == 0)
        {
            throw new SolutionWorkspaceLoadException(solutionPath, "No supported projects were found.");
        }

        var rootBuilder = new MutableNode(SolutionTreeNodeKind.Solution, Path.GetFileName(solutionPath), solutionPath);
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

        return new SolutionWorkspaceSnapshot(solutionPath, rootBuilder.ToImmutable());
    }

    private static List<ProjectLoadResult> LoadProjectsWithBuildalyzer(
        string solutionPath,
        SolutionFileInfo solutionInfo,
        CancellationToken cancellationToken)
    {
        AnalyzerManager manager;
        try
        {
            manager = solutionInfo.ProjectPaths.Count > 0
                ? new AnalyzerManager()
                : new AnalyzerManager(solutionPath);
        }
        catch (Exception ex)
        {
            throw new SolutionWorkspaceLoadException(solutionPath, "Buildalyzer could not open this solution.", ex);
        }

        var projects = GetProjects(manager, solutionInfo);
        var solutionProjectPaths = projects
            .Select(project => project.ProjectPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<ProjectLoadResult>();
        foreach (var (projectPath, analyzer) in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(projectPath) || IsHiddenPath(projectPath))
            {
                continue;
            }

            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var projectBuilder = new MutableNode(SolutionTreeNodeKind.Project, projectName, projectPath);
            AddDependencies(projectBuilder, projectPath, solutionProjectPaths);
            projectBuilder.Children.Add(new MutableNode(SolutionTreeNodeKind.File, Path.GetFileName(projectPath), projectPath));

            foreach (var file in GetProjectFiles(projectPath, analyzer))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddProjectFile(projectBuilder, projectPath, file);
            }

            var folderPath = solutionInfo.GetFolderPath(projectPath);
            results.Add(new ProjectLoadResult(projectName, folderPath, projectBuilder));
        }

        return results;
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

            projects.Add((projectPath, manager.GetProject(projectPath)));
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
        for (var index = 0; index < parts.Length - 1; index++)
        {
            parent = parent.GetOrAddChild(SolutionTreeNodeKind.Folder, parts[index], null);
        }

        parent.GetOrAddChild(SolutionTreeNodeKind.File, parts[^1], filePath);
    }

    private static bool IsVisibleFile(string path)
    {
        return File.Exists(path) && !IsHiddenPath(path);
    }

    private static bool IsHiddenPath(string path)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   .Any(segment => HiddenPathSegments.Contains(segment));
    }

    private sealed record ProjectLoadResult(
        string Name,
        IReadOnlyList<string> SolutionFolderPath,
        MutableNode Node);

    private sealed record ProjectReferenceInfo(string Include);

    private sealed record PackageReferenceInfo(string Id, string? Version);

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

        public MutableNode GetOrAddChild(SolutionTreeNodeKind kind, string name, string? path)
        {
            var existing = Children.FirstOrDefault(child =>
                child.Kind == kind &&
                string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(child.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }

            var child = new MutableNode(kind, name, path);
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

        public static SolutionFileInfo Parse(string solutionPath)
        {
            var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
            var projectIdByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var folderNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parentIdById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var slnxFolderPathByProjectPath = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

            if (!string.Equals(Path.GetExtension(solutionPath), ".sln", StringComparison.OrdinalIgnoreCase))
            {
                var slnxProjectPaths = ParseSlnx(solutionPath, solutionDirectory, slnxFolderPathByProjectPath);
                return new SolutionFileInfo(
                    solutionDirectory,
                    projectIdByPath,
                    folderNameById,
                    parentIdById,
                    slnxFolderPathByProjectPath)
                {
                    ProjectPaths = slnxProjectPaths,
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
                    folders.Push(folderName);
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
            var document = XDocument.Load(solutionPath);
            var projects = new List<string>();
            var root = document.Root;
            if (root is null)
            {
                return projects;
            }

            ReadSlnxElement(root, solutionDirectory, [], folderPathByProjectPath, projects);
            return projects;
        }

        private static void ReadSlnxElement(
            XElement element,
            string solutionDirectory,
            IReadOnlyList<string> folderPath,
            Dictionary<string, IReadOnlyList<string>> folderPathByProjectPath,
            List<string> projects)
        {
            foreach (var child in element.Elements())
            {
                if (string.Equals(child.Name.LocalName, "Folder", StringComparison.OrdinalIgnoreCase))
                {
                    var folderName = (string?)child.Attribute("Name") ?? string.Empty;
                    var childFolderPath = folderPath.Concat(SplitSlnxFolderName(folderName)).ToArray();
                    ReadSlnxElement(child, solutionDirectory, childFolderPath, folderPathByProjectPath, projects);
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

        private static IReadOnlyList<string> SplitSlnxFolderName(string folderName)
        {
            return folderName
                .Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();
        }
    }
}
