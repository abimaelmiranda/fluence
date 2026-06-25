using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.Workbench.SolutionView.Abstractions;
using Fluence.Modules.Workbench.SolutionView.Models;
using Fluence.Modules.Workbench.SolutionView.Models.Enums;

namespace Fluence.Modules.Workbench.SolutionView.Services;

public sealed class SolutionProjectAssociationService : IProjectAssociationService
{
    private readonly object _gate = new();
    private string? _solutionPath;
    private Dictionary<string, string> _projectByFilePath = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _projectByDirectoryPath = new(StringComparer.OrdinalIgnoreCase);

    public string? FindProjectForFile(string solutionPath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(solutionPath) || string.IsNullOrWhiteSpace(filePath))
            return null;

        var normalizedSolutionPath = NormalizePath(solutionPath);
        var normalizedFilePath = NormalizePath(filePath);

        lock (_gate)
        {
            if (!string.Equals(_solutionPath, normalizedSolutionPath, StringComparison.OrdinalIgnoreCase))
                return null;

            if (_projectByFilePath.TryGetValue(normalizedFilePath, out var projectPath))
                return projectPath;

            return FindProjectByContainingDirectory(normalizedFilePath, _projectByDirectoryPath);
        }
    }

    public void UpdateStructural(SolutionWorkspaceSnapshot snapshot)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddStructuralAssociations(snapshot.Root, files, directories);

        lock (_gate)
        {
            _solutionPath = NormalizePath(snapshot.SolutionPath);
            _projectByFilePath = files;
            _projectByDirectoryPath = directories;
        }
    }

    public void UpdateProject(string solutionPath, SolutionTreeNode projectNode)
    {
        if (projectNode.Kind != SolutionTreeNodeKind.Project || string.IsNullOrWhiteSpace(projectNode.Path))
            return;

        var normalizedSolutionPath = NormalizePath(solutionPath);
        var normalizedProjectPath = NormalizePath(projectNode.Path);
        var projectDirectory = Path.GetDirectoryName(normalizedProjectPath);

        lock (_gate)
        {
            if (!string.Equals(_solutionPath, normalizedSolutionPath, StringComparison.OrdinalIgnoreCase))
                return;

            RemoveProjectAssociations(normalizedProjectPath);
            _projectByFilePath[normalizedProjectPath] = normalizedProjectPath;
            if (!string.IsNullOrWhiteSpace(projectDirectory))
                _projectByDirectoryPath[NormalizePath(projectDirectory)] = normalizedProjectPath;

            AddFileAssociations(projectNode, normalizedProjectPath, _projectByFilePath);
        }
    }

    public void Clear(string? solutionPath = null)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(solutionPath) &&
                !string.Equals(_solutionPath, NormalizePath(solutionPath), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _solutionPath = null;
            _projectByFilePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _projectByDirectoryPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void AddStructuralAssociations(
        SolutionTreeNode node,
        Dictionary<string, string> files,
        Dictionary<string, string> directories)
    {
        if (node.Kind == SolutionTreeNodeKind.Project && !string.IsNullOrWhiteSpace(node.Path))
        {
            var projectPath = NormalizePath(node.Path);
            files.TryAdd(projectPath, projectPath);

            var projectDirectory = Path.GetDirectoryName(projectPath);
            if (!string.IsNullOrWhiteSpace(projectDirectory))
                directories.TryAdd(NormalizePath(projectDirectory), projectPath);
        }

        foreach (var child in node.Children)
            AddStructuralAssociations(child, files, directories);
    }

    private static void AddFileAssociations(
        SolutionTreeNode node,
        string currentProjectPath,
        Dictionary<string, string> map)
    {
        if (node.Kind == SolutionTreeNodeKind.File && !string.IsNullOrWhiteSpace(node.Path))
            map[NormalizePath(node.Path)] = currentProjectPath;

        foreach (var child in node.Children)
            AddFileAssociations(child, currentProjectPath, map);
    }

    private void RemoveProjectAssociations(string projectPath)
    {
        _projectByFilePath = _projectByFilePath
            .Where(entry => !string.Equals(entry.Value, projectPath, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        _projectByDirectoryPath = _projectByDirectoryPath
            .Where(entry => !string.Equals(entry.Value, projectPath, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string? FindProjectByContainingDirectory(
        string filePath,
        Dictionary<string, string> projectByDirectoryPath)
    {
        var directory = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var normalizedDirectory = NormalizePath(directory);
            if (projectByDirectoryPath.TryGetValue(normalizedDirectory, out var projectPath))
                return projectPath;

            directory = Directory.GetParent(normalizedDirectory)?.FullName;
        }

        return null;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}
