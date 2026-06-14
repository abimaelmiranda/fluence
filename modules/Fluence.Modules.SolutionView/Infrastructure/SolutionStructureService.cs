using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Fluence.Core.Ports;

namespace Fluence.Modules.SolutionView;

public sealed class SolutionStructureService(IFileService fileService) : ISolutionStructureService
{
    private const string SolutionFolderTypeGuid = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";

    private static readonly Regex ProjectLinePattern = new(
        "^Project\\(\"(?<type>[^\"]+)\"\\) = \"(?<name>[^\"]+)\", \"(?<path>[^\"]+)\", \"(?<id>[^\"]+)\"",
        RegexOptions.Compiled);

    private static readonly XmlWriterSettings XmlSaveSettings = new()
    {
        OmitXmlDeclaration = true,
        Indent = true,
        IndentChars = "  ",
    };

    public Task CreatePhysicalFolderAsync(
        string projectPath,
        string parentDirectory,
        string folderName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("A project path is required.", nameof(projectPath));
        if (string.IsNullOrWhiteSpace(parentDirectory))
            throw new ArgumentException("A parent directory is required.", nameof(parentDirectory));
        if (string.IsNullOrWhiteSpace(folderName))
            throw new ArgumentException("A folder name is required.", nameof(folderName));

        var physicalPath = Path.Combine(parentDirectory, folderName);

        fileService.CreateDirectory(physicalPath);

        try
        {
            AddFolderToCsproj(projectPath, physicalPath);
        }
        catch
        {
            TryRemoveEmptyDirectory(physicalPath);
            throw;
        }

        return Task.CompletedTask;
    }

    public Task CreateSolutionFolderAsync(
        string solutionPath,
        string virtualParentPath,
        string folderName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(solutionPath))
            throw new ArgumentException("A solution path is required.", nameof(solutionPath));
        if (string.IsNullOrWhiteSpace(folderName))
            throw new ArgumentException("A folder name is required.", nameof(folderName));

        var parentSegments = SplitFolderPath(virtualParentPath);
        var newSegments = parentSegments.Concat(SplitFolderPath(folderName)).ToArray();

        if (newSegments.Length == 0)
            throw new InvalidOperationException("The resulting folder path is invalid.");

        if (string.Equals(Path.GetExtension(solutionPath), ".slnx", StringComparison.OrdinalIgnoreCase))
        {
            UpdateSlnx(solutionPath, newSegments);
        }
        else
        {
            var normalizedPath = NormalizeSolutionFolderPath(string.Join("/", newSegments));
            UpdateSln(solutionPath, normalizedPath);
        }

        return Task.CompletedTask;
    }

    public Task RemovePhysicalFolderAsync(
        string projectPath,
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("A project path is required.", nameof(projectPath));
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("A folder path is required.", nameof(folderPath));

        RemoveFolderFromCsproj(projectPath, folderPath);
        return Task.CompletedTask;
    }

    private static void RemoveFolderFromCsproj(string projectPath, string folderPath)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        var csprojDir = Path.GetDirectoryName(fullProjectPath)
            ?? throw new InvalidOperationException("Cannot resolve .csproj directory.");

        var prefix = Path.GetRelativePath(csprojDir, folderPath).Replace('/', '\\').TrimEnd('\\') + '\\';

        var document = XDocument.Load(fullProjectPath);
        var root = document.Root ?? throw new InvalidOperationException("The project file has no root element.");

        var ns = root.Name.Namespace;

        var toRemove = root
            .Descendants(ns + "Folder")
            .Where(e =>
            {
                var include = ((string?)e.Attribute("Include") ?? string.Empty).Replace('/', '\\');
                return include.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (toRemove.Count == 0)
            return;

        foreach (var element in toRemove)
        {
            var parent = element.Parent;
            element.Remove();
            if (parent is not null && !parent.HasElements && IsElement(parent, "ItemGroup"))
                parent.Remove();
        }

        using var writer = XmlWriter.Create(fullProjectPath, XmlSaveSettings);
        document.Save(writer);
    }

    private static void AddFolderToCsproj(string projectPath, string physicalPath)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        var csprojDir = Path.GetDirectoryName(fullProjectPath)
            ?? throw new InvalidOperationException("Cannot resolve .csproj directory.");

        var include = Path.GetRelativePath(csprojDir, physicalPath).Replace('/', '\\') + '\\';

        var document = XDocument.Load(fullProjectPath);
        var root = document.Root ?? throw new InvalidOperationException("The project file has no root element.");

        var ns = root.Name.Namespace;

        var alreadyPresent = root
            .Descendants(ns + "Folder")
            .Any(e => string.Equals((string?)e.Attribute("Include"), include, StringComparison.OrdinalIgnoreCase));

        if (alreadyPresent)
            return;

        var itemGroup = root.Elements(ns + "ItemGroup").LastOrDefault();
        if (itemGroup is null)
        {
            itemGroup = new XElement(ns + "ItemGroup");
            root.Add(itemGroup);
        }

        itemGroup.Add(new XElement(ns + "Folder", new XAttribute("Include", include)));

        using var writer = XmlWriter.Create(fullProjectPath, XmlSaveSettings);
        document.Save(writer);
    }

    private static void UpdateSlnx(string solutionPath, IReadOnlyList<string> folderSegments)
    {
        var document = XDocument.Load(solutionPath);
        var root = document.Root ?? throw new InvalidOperationException("The solution file has no root element.");

        var normalizedFolderPath = NormalizeSolutionFolderPath(string.Join("/", folderSegments));
        if (HasSlnxFolder(root, normalizedFolderPath))
            return;

        EnsureSlnxFolder(root, folderSegments, 0);

        using var writer = XmlWriter.Create(solutionPath, XmlSaveSettings);
        document.Save(writer);
    }

    private static void UpdateSln(string solutionPath, string normalizedFolderPath)
    {
        var lines = File.ReadAllLines(solutionPath).ToList();
        if (ParseSlnFolderPaths(lines).Contains(normalizedFolderPath, StringComparer.OrdinalIgnoreCase))
            return;

        var projectInsertIndex = lines.FindIndex(line => line.TrimStart().StartsWith("Global", StringComparison.Ordinal));
        if (projectInsertIndex < 0)
            throw new InvalidOperationException("The solution file does not contain a Global section.");

        var folderId = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var projectLines = new[]
        {
            $"Project(\"{SolutionFolderTypeGuid}\") = \"{normalizedFolderPath}\", \"{normalizedFolderPath}\", \"{folderId}\"",
            "EndProject",
        };
        lines.InsertRange(projectInsertIndex, projectLines);
        File.WriteAllLines(solutionPath, lines);
    }

    private static string NormalizeSolutionFolderPath(string path)
    {
        return string.Join("/", SplitFolderPath(path));
    }

    private static IReadOnlyList<string> SplitFolderPath(string path)
    {
        return path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Where(segment => !string.IsNullOrWhiteSpace(segment))
                   .ToArray();
    }

    private static bool HasSlnxFolder(XElement parent, string folderPath)
    {
        foreach (var candidate in EnumerateSlnxFolderPaths(parent, Array.Empty<string>()))
        {
            if (string.Equals(candidate, folderPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static XElement EnsureSlnxFolder(XElement parent, IReadOnlyList<string> folderSegments, int segmentIndex)
    {
        if (segmentIndex >= folderSegments.Count)
            return parent;

        var matchingChild = FindMatchingSlnxFolderChild(parent, folderSegments, segmentIndex);
        if (matchingChild is null)
        {
            matchingChild = new XElement(
                parent.Name.Namespace + "Folder",
                new XAttribute("Name", CreateSlnxFolderName(folderSegments[segmentIndex], parent)));
            parent.Add(matchingChild);
            return EnsureSlnxFolder(matchingChild, folderSegments, segmentIndex + 1);
        }

        var childSegments = SplitFolderPath((string?)matchingChild.Attribute("Name") ?? string.Empty);
        return EnsureSlnxFolder(matchingChild, folderSegments, segmentIndex + childSegments.Count);
    }

    private static XElement? FindMatchingSlnxFolderChild(
        XElement parent,
        IReadOnlyList<string> folderSegments,
        int segmentIndex)
    {
        XElement? bestMatch = null;
        var bestMatchLength = 0;

        foreach (var child in parent.Elements())
        {
            if (!IsElement(child, "Folder"))
                continue;

            var childSegments = SplitFolderPath((string?)child.Attribute("Name") ?? string.Empty);
            if (childSegments.Count == 0 || segmentIndex + childSegments.Count > folderSegments.Count)
                continue;

            var isMatch = true;
            for (var index = 0; index < childSegments.Count; index++)
            {
                if (!string.Equals(
                        childSegments[index],
                        folderSegments[segmentIndex + index],
                        StringComparison.OrdinalIgnoreCase))
                {
                    isMatch = false;
                    break;
                }
            }

            if (isMatch && childSegments.Count > bestMatchLength)
            {
                bestMatch = child;
                bestMatchLength = childSegments.Count;
            }
        }

        return bestMatch;
    }

    private static string CreateSlnxFolderName(string folderSegment, XElement parent)
    {
        if (parent.Parent is null)
            return $"/{folderSegment}/";

        return folderSegment;
    }

    private static IEnumerable<string> EnumerateSlnxFolderPaths(XElement element, IReadOnlyList<string> parentPath)
    {
        foreach (var child in element.Elements())
        {
            if (!IsElement(child, "Folder"))
                continue;

            var folderName = (string?)child.Attribute("Name") ?? string.Empty;
            var childPath = parentPath.Concat(SplitFolderPath(folderName)).ToArray();
            if (childPath.Length > 0)
                yield return NormalizeSolutionFolderPath(string.Join("/", childPath));

            foreach (var nested in EnumerateSlnxFolderPaths(child, childPath))
                yield return nested;
        }
    }

    private static IReadOnlyList<string> ParseSlnFolderPaths(IReadOnlyList<string> lines)
    {
        var folderNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var parentById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inNestedProjects = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            var match = ProjectLinePattern.Match(line);
            if (match.Success)
            {
                var id = match.Groups["id"].Value;
                var type = match.Groups["type"].Value;
                var name = match.Groups["name"].Value;
                if (string.Equals(type, SolutionFolderTypeGuid, StringComparison.OrdinalIgnoreCase))
                    folderNamesById[id] = name;

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
                continue;

            var parts = line.Split('=', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
                parentById[parts[0]] = parts[1];
        }

        var paths = new List<string>();
        foreach (var id in folderNamesById.Keys)
            paths.Add(GetSlnFolderPath(id, folderNamesById, parentById));

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetSlnFolderPath(
        string id,
        IReadOnlyDictionary<string, string> folderNamesById,
        IReadOnlyDictionary<string, string> parentById)
    {
        var segments = new Stack<string>();
        var currentId = id;

        while (true)
        {
            if (folderNamesById.TryGetValue(currentId, out var name))
            {
                foreach (var segment in SplitFolderPath(name).Reverse())
                    segments.Push(segment);
            }

            if (!parentById.TryGetValue(currentId, out var parentId))
                break;

            currentId = parentId;
        }

        return string.Join("/", segments);
    }

    private static void TryRemoveEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch
        {
        }
    }

    private static bool IsElement(XElement element, string localName)
        => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);
}
