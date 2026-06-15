using System.Text.RegularExpressions;
using System.Xml.Linq;
using Fluence.Modules.NuGetExplorer.Abstractions;
using Fluence.Modules.NuGetExplorer.Models;

namespace Fluence.Modules.NuGetExplorer.Services;

public sealed class NuGetProjectService : INuGetProjectService
{
    private static readonly Regex ProjectLinePattern = new(
        "^Project\\(\"(?<type>[^\"]+)\"\\) = \"(?<name>[^\"]+)\", \"(?<path>[^\"]+)\", \"(?<id>[^\"]+)\"",
        RegexOptions.Compiled);

    private const string PackageReferenceElementName = "PackageReference";
    private const string PackageVersionElementName = "PackageVersion";
    private const string DirectoryPackagesFileName = "Directory.Packages.props";

    public bool UsesCentralPackageManagement(string solutionPath)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath);
        if (string.IsNullOrWhiteSpace(solutionDirectory))
        {
            return false;
        }

        var directory = new DirectoryInfo(solutionDirectory);
        while (directory is not null)
        {
            var centralPackageFile = Path.Combine(directory.FullName, DirectoryPackagesFileName);
            if (File.Exists(centralPackageFile) && ContainsPackageVersion(centralPackageFile))
            {
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    public Task<IReadOnlyList<NuGetProject>> GetProjectsAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var projects = GetProjectPaths(solutionPath)
            .Where(File.Exists)
            .Select(path => new NuGetProject(path, Path.GetFileNameWithoutExtension(path)))
            .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<NuGetProject>>(projects);
    }

    public async Task<IReadOnlyList<NuGetInstalledPackage>> GetInstalledPackagesAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var projects = await GetProjectsAsync(solutionPath, cancellationToken);
        var packages = new List<NuGetInstalledPackage>();

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            packages.AddRange(ReadPackageReferences(project));
        }

        return packages
            .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task InstallPackageAsync(
        string projectPath,
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedProjectPath = Path.GetFullPath(projectPath);
        var document = XDocument.Load(normalizedProjectPath, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException("The project file has no root element.");
        var existingReference = root.Descendants()
            .FirstOrDefault(element => IsPackageReference(element, packageId));

        if (existingReference is not null)
        {
            SetPackageVersion(existingReference, version);
            document.Save(normalizedProjectPath);
            return Task.CompletedTask;
        }

        var itemGroup = root.Elements()
            .FirstOrDefault(element => IsElement(element, "ItemGroup") &&
                                       element.Elements().Any(child => IsElement(child, PackageReferenceElementName)))
                        ?? new XElement(root.Name.Namespace + "ItemGroup");

        if (itemGroup.Parent is null)
        {
            root.Add(new XText(Environment.NewLine + "  "));
            root.Add(itemGroup);
            root.Add(new XText(Environment.NewLine));
        }

        itemGroup.Add(new XText(Environment.NewLine + "    "));
        itemGroup.Add(new XElement(
            root.Name.Namespace + PackageReferenceElementName,
            new XAttribute("Include", packageId),
            new XAttribute("Version", version)));
        itemGroup.Add(new XText(Environment.NewLine + "  "));
        document.Save(normalizedProjectPath);
        return Task.CompletedTask;
    }

    public Task RemovePackageAsync(
        string projectPath,
        string packageId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedProjectPath = Path.GetFullPath(projectPath);
        var document = XDocument.Load(normalizedProjectPath, LoadOptions.PreserveWhitespace);
        var references = document.Descendants()
            .Where(element => IsPackageReference(element, packageId))
            .ToArray();

        foreach (var reference in references)
        {
            reference.Remove();
        }

        document.Save(normalizedProjectPath);
        return Task.CompletedTask;
    }

    private static IReadOnlyList<NuGetInstalledPackage> ReadPackageReferences(NuGetProject project)
    {
        try
        {
            var document = XDocument.Load(project.ProjectPath);
            return document.Descendants()
                .Where(element => IsElement(element, PackageReferenceElementName))
                .Select(element =>
                {
                    var id = (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update");
                    var version = GetPackageVersion(element);
                    return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version)
                        ? null
                        : new NuGetInstalledPackage(project.ProjectPath, project.Name, id, version);
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

    private static IReadOnlyList<string> GetProjectPaths(string solutionPath)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
        if (string.Equals(Path.GetExtension(solutionPath), ".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return GetSlnxProjectPaths(solutionPath, solutionDirectory);
        }

        return File.ReadLines(solutionPath)
            .Select(line => ProjectLinePattern.Match(line.Trim()))
            .Where(match => match.Success)
            .Select(match => match.Groups["path"].Value)
            .Where(path => path.EndsWith("proj", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFullPath(Path.Combine(solutionDirectory, path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> GetSlnxProjectPaths(string solutionPath, string solutionDirectory)
    {
        var document = XDocument.Load(solutionPath);
        return document.Descendants()
            .Where(element => IsElement(element, "Project"))
            .Select(element => (string?)element.Attribute("Path"))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Path.Combine(solutionDirectory, path!)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ContainsPackageVersion(string path)
    {
        try
        {
            var document = XDocument.Load(path);
            return document.Descendants().Any(element => IsElement(element, PackageVersionElementName));
        }
        catch
        {
            return true;
        }
    }

    private static bool IsPackageReference(XElement element, string packageId)
    {
        if (!IsElement(element, PackageReferenceElementName))
        {
            return false;
        }

        var id = (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update");
        return string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetPackageVersion(XElement element)
    {
        return (string?)element.Attribute("Version") ??
               element.Elements().FirstOrDefault(child => IsElement(child, "Version"))?.Value;
    }

    private static void SetPackageVersion(XElement element, string version)
    {
        var versionAttribute = element.Attribute("Version");
        if (versionAttribute is not null)
        {
            versionAttribute.Value = version;
            return;
        }

        var versionElement = element.Elements().FirstOrDefault(child => IsElement(child, "Version"));
        if (versionElement is not null)
        {
            versionElement.Value = version;
            return;
        }

        element.Add(new XAttribute("Version", version));
    }

    private static bool IsElement(XElement element, string localName)
        => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);
}
