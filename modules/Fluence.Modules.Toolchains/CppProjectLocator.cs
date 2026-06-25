using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Modules.Toolchains;

internal static class CppProjectLocator
{
    internal static CppProjectContext? ResolveFromWorkspace(IWorkspaceContext workspace) =>
        ResolveFromPath(GetStartPath(workspace.Current));

    internal static CppProjectContext? ResolveFromPath(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
            return null;

        var directory = Directory.Exists(startPath) ? startPath : Path.GetDirectoryName(startPath);
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        directory = Path.GetFullPath(directory);
        var root = FindProjectRoot(directory);
        if (root is null)
        {
            if (!IsNativeSourceFile(startPath))
                return null;

            root = directory;
        }

        var cmakeLists = Path.Combine(root, "CMakeLists.txt");
        var hasCMakeLists = File.Exists(cmakeLists);
        var projectName = TryReadProjectName(cmakeLists) ?? Path.GetFileName(root);
        var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? projectName + ".exe"
            : projectName;

        return new CppProjectContext(
            ProjectRoot: root,
            BuildDirectory: Path.Combine(root, "build"),
            ProjectName: projectName,
            ExecutableName: executableName,
            HasCMakeLists: hasCMakeLists);
    }

    internal static string? FindExecutablePath(string buildDir, CppProjectContext context)
    {
        if (!Directory.Exists(buildDir))
            return null;

        var direct = Directory.EnumerateFiles(buildDir, context.ExecutableName, SearchOption.AllDirectories)
            .FirstOrDefault();
        if (direct is not null)
            return direct;

        return Directory.EnumerateFiles(buildDir, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path =>
                string.Equals(Path.GetFileNameWithoutExtension(path), context.ProjectName, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(Path.GetExtension(path)) ||
                 string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)));
    }

    private static string? GetStartPath(Workspace current)
    {
        return current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument
            ? current.TabSession.ActiveDocument.Path
            : current.Mode == WorkspaceMode.Folder
                ? current.CurrentFolderPath
                : current.Mode == WorkspaceMode.Solution && current.CurrentSolutionPath is not null
                    ? Path.GetDirectoryName(current.CurrentSolutionPath)
                    : current.CurrentFilePath;
    }

    private static string? FindProjectRoot(string directory)
    {
        var current = directory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "CMakeLists.txt")) ||
                Directory.EnumerateFiles(current, "*.vcxproj", SearchOption.TopDirectoryOnly).Any())
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null || string.Equals(parent.FullName, current, StringComparison.OrdinalIgnoreCase))
                return null;

            current = parent.FullName;
        }

        return null;
    }

    private static bool IsNativeSourceFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".c", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".cpp", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".cc", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".cxx", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".h", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".hpp", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".hh", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".hxx", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryReadProjectName(string cmakeListsPath)
    {
        if (!File.Exists(cmakeListsPath))
            return null;

        foreach (var line in File.ReadLines(cmakeListsPath))
        {
            var match = Regex.Match(line, @"^\s*project\s*\(\s*([A-Za-z0-9_\-.]+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value;
        }

        return null;
    }
}

internal sealed record CppProjectContext(
    string ProjectRoot,
    string BuildDirectory,
    string ProjectName,
    string ExecutableName,
    bool HasCMakeLists);
