using System;
using System.Collections.Generic;
using System.IO;
using Fluence.Core.Workspace;
using Fluence.Modules.SolutionView.Abstractions;
using Fluence.Modules.SolutionView.Models;
using Fluence.Modules.SolutionView.Models.Enums;

namespace Fluence.Modules.SolutionView.Services;

public sealed class SolutionProjectAssociationService : IProjectAssociationService
{
    private readonly object _gate = new();
    private string? _solutionPath;
    private Dictionary<string, string> _projectByFilePath = new(StringComparer.OrdinalIgnoreCase);

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

            return _projectByFilePath.TryGetValue(normalizedFilePath, out var projectPath)
                ? projectPath
                : null;
        }
    }

    public void Update(SolutionWorkspaceSnapshot snapshot)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddAssociations(snapshot.Root, currentProjectPath: null, map);

        lock (_gate)
        {
            _solutionPath = NormalizePath(snapshot.SolutionPath);
            _projectByFilePath = map;
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
        }
    }

    private static void AddAssociations(
        SolutionTreeNode node,
        string? currentProjectPath,
        Dictionary<string, string> map)
    {
        if (node.Kind == SolutionTreeNodeKind.Project && !string.IsNullOrWhiteSpace(node.Path))
        {
            currentProjectPath = NormalizePath(node.Path);
            map.TryAdd(currentProjectPath, currentProjectPath);
        }
        else if (node.Kind == SolutionTreeNodeKind.File &&
                 !string.IsNullOrWhiteSpace(node.Path) &&
                 !string.IsNullOrWhiteSpace(currentProjectPath))
        {
            map.TryAdd(NormalizePath(node.Path), currentProjectPath);
        }

        foreach (var child in node.Children)
            AddAssociations(child, currentProjectPath, map);
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
