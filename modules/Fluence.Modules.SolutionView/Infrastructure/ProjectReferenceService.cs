using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Fluence.Modules.SolutionView;

public sealed class ProjectReferenceService : IProjectReferenceService
{
    public Task<IReadOnlyList<ProjectReferenceCandidate>> GetReferenceCandidatesAsync(
        string projectPath,
        SolutionWorkspaceSnapshot solutionSnapshot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedProjectPath = NormalizePath(projectPath);
        var existingReferences = ReadProjectReferences(normalizedProjectPath)
            .Select(reference => NormalizeProjectReferencePath(normalizedProjectPath, reference))
            .Where(path => path is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = GetProjectNodes(solutionSnapshot.Root)
            .Where(project => project.Path is not null)
            .Select(project =>
            {
                var candidatePath = NormalizePath(project.Path!);
                return new ProjectReferenceCandidate(
                    candidatePath,
                    project.Name,
                    existingReferences.Contains(candidatePath));
            })
            .Where(candidate => !string.Equals(candidate.ProjectPath, normalizedProjectPath, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => candidate.IsReferenced || !HasDirectProjectReference(candidate.ProjectPath, normalizedProjectPath))
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ProjectReferenceCandidate>>(candidates);
    }

    public Task AddProjectReferencesAsync(
        string projectPath,
        IReadOnlyList<string> referencedProjectPaths,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (referencedProjectPaths.Count == 0)
        {
            return Task.CompletedTask;
        }

        var normalizedProjectPath = NormalizePath(projectPath);
        var document = XDocument.Load(normalizedProjectPath, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException("The project file has no root element.");
        var projectDirectory = Path.GetDirectoryName(normalizedProjectPath) ?? Directory.GetCurrentDirectory();
        var existingReferences = root.Descendants()
            .Where(element => IsElement(element, "ProjectReference"))
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => NormalizeProjectReferencePath(normalizedProjectPath, include!))
            .Where(path => path is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var referencesToAdd = referencedProjectPaths
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(reference => !string.Equals(reference, normalizedProjectPath, StringComparison.OrdinalIgnoreCase))
            .Where(reference => !existingReferences.Contains(reference))
            .Where(reference => !HasDirectProjectReference(reference, normalizedProjectPath))
            .ToArray();

        if (referencesToAdd.Length == 0)
        {
            return Task.CompletedTask;
        }

        var itemGroup = root.Elements()
            .FirstOrDefault(element => IsElement(element, "ItemGroup") &&
                                       element.Elements().Any(child => IsElement(child, "ProjectReference")))
                        ?? new XElement(root.Name.Namespace + "ItemGroup");

        if (itemGroup.Parent is null)
        {
            root.Add(new XText(Environment.NewLine + "  "));
            root.Add(itemGroup);
            root.Add(new XText(Environment.NewLine));
        }

        foreach (var referencedProjectPath in referencesToAdd)
        {
            var include = Path.GetRelativePath(projectDirectory, referencedProjectPath);
            itemGroup.Add(new XText(Environment.NewLine + "    "));
            itemGroup.Add(new XElement(root.Name.Namespace + "ProjectReference", new XAttribute("Include", include)));
        }

        itemGroup.Add(new XText(Environment.NewLine + "  "));
        document.Save(normalizedProjectPath);
        return Task.CompletedTask;
    }

    public Task RemoveProjectReferenceAsync(
        string projectPath,
        string referencedProjectPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedProjectPath = NormalizePath(projectPath);
        var normalizedReferencedProjectPath = NormalizePath(referencedProjectPath);
        var document = XDocument.Load(normalizedProjectPath, LoadOptions.PreserveWhitespace);
        var references = document.Descendants()
            .Where(element => IsElement(element, "ProjectReference"))
            .Where(element =>
            {
                var include = (string?)element.Attribute("Include");
                var path = string.IsNullOrWhiteSpace(include)
                    ? null
                    : NormalizeProjectReferencePath(normalizedProjectPath, include);
                return string.Equals(path, normalizedReferencedProjectPath, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        if (references.Length == 0)
        {
            return Task.CompletedTask;
        }

        foreach (var reference in references)
        {
            reference.Remove();
        }

        document.Save(normalizedProjectPath);
        return Task.CompletedTask;
    }

    private static bool HasDirectProjectReference(string projectPath, string referencedProjectPath)
    {
        if (!File.Exists(projectPath))
        {
            return false;
        }

        return ReadProjectReferences(projectPath)
            .Select(reference => NormalizeProjectReferencePath(projectPath, reference))
            .Any(path => string.Equals(path, NormalizePath(referencedProjectPath), StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ReadProjectReferences(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            return [];
        }

        var document = XDocument.Load(projectPath);
        return document.Descendants()
            .Where(element => IsElement(element, "ProjectReference"))
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .ToArray();
    }

    private static string? NormalizeProjectReferencePath(string projectPath, string include)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return null;
        }

        var normalizedInclude = NormalizeProjectInclude(include);
        var path = Path.IsPathRooted(normalizedInclude)
            ? normalizedInclude
            : Path.Combine(projectDirectory, normalizedInclude);
        return NormalizePath(path);
    }

    private static string NormalizeProjectInclude(string include)
        => include.Replace('\\', Path.DirectorySeparatorChar)
                  .Replace('/', Path.DirectorySeparatorChar);

    private static IEnumerable<SolutionTreeNode> GetProjectNodes(SolutionTreeNode node)
    {
        if (node.Kind == SolutionTreeNodeKind.Project)
        {
            yield return node;
        }

        foreach (var child in node.Children)
        {
            foreach (var project in GetProjectNodes(child))
            {
                yield return project;
            }
        }
    }

    private static bool IsElement(XElement element, string localName)
        => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path)
        => Path.GetFullPath(path);
}
