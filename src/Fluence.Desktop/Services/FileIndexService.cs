using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Desktop.ViewModels;

namespace Fluence.Desktop.Services;

public sealed class FileIndexService : IDisposable
{
    private static readonly HashSet<string> ExcludedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", ".vscode", "node_modules", "packages", ".nuget",
    };

    private readonly IWorkspaceContext _workspace;
    private IReadOnlyList<string> _indexedFiles = [];
    private string? _indexedRoot;
    private CancellationTokenSource _cts = new();

    public FileIndexService(IWorkspaceContext workspace)
    {
        _workspace = workspace;
        _workspace.Changed += OnWorkspaceChanged;
        TriggerIndex();
    }

    public IReadOnlyList<FileSearchResult> Search(string query)
    {
        var files = _indexedFiles;
        if (files.Count == 0)
            return [];

        if (string.IsNullOrWhiteSpace(query))
            return files.Take(20).Select(f => ToResult(f, _indexedRoot)).ToList();

        var q = query.Trim();
        return files
            .Select(path => (path, score: Score(path, q)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(20)
            .Select(x => ToResult(x.path, _indexedRoot))
            .ToList();
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e) => TriggerIndex();

    private void TriggerIndex()
    {
        var root = GetRoot();
        if (root is null || string.Equals(root, _indexedRoot, StringComparison.OrdinalIgnoreCase))
            return;

        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(() => IndexAsync(root, token), token);
    }

    private string? GetRoot()
    {
        var ws = _workspace.Current;
        return ws.Mode switch
        {
            WorkspaceMode.Solution when ws.CurrentSolutionPath is not null =>
                Path.GetDirectoryName(ws.CurrentSolutionPath),
            WorkspaceMode.Folder => ws.CurrentFolderPath,
            _ => null,
        };
    }

    private void IndexAsync(string root, CancellationToken cancellationToken)
    {
        try
        {
            var files = new List<string>(512);
            EnumerateFiles(root, files, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                _indexedFiles = files;
                _indexedRoot = root;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileIndexService] Indexing failed: {ex.Message}");
        }
    }

    private static void EnumerateFiles(string directory, List<string> result, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
                result.Add(file);
            }

            foreach (var subDir in Directory.EnumerateDirectories(directory))
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                var name = Path.GetFileName(subDir);
                if (ExcludedFolders.Contains(name))
                    continue;

                EnumerateFiles(subDir, result, cancellationToken);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static int Score(string absolutePath, string query)
    {
        var fileName = Path.GetFileName(absolutePath);
        var q = query;

        if (string.Equals(fileName, q, StringComparison.OrdinalIgnoreCase))
            return 100;

        if (fileName.StartsWith(q, StringComparison.OrdinalIgnoreCase))
            return 80;

        if (fileName.Contains(q, StringComparison.OrdinalIgnoreCase))
            return 60;

        if (absolutePath.Contains(q, StringComparison.OrdinalIgnoreCase))
            return 40;

        if (HasSequentialChars(fileName, q))
            return 20;

        return 0;
    }

    private static bool HasSequentialChars(string source, string query)
    {
        var si = 0;
        var qi = 0;
        while (si < source.Length && qi < query.Length)
        {
            if (char.ToLowerInvariant(source[si]) == char.ToLowerInvariant(query[qi]))
                qi++;
            si++;
        }
        return qi == query.Length;
    }

    private static FileSearchResult ToResult(string absolutePath, string? root)
    {
        var fileName = Path.GetFileName(absolutePath);
        var relative = root is not null && absolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? absolutePath[(root.Length + 1)..]
            : absolutePath;
        return new FileSearchResult(absolutePath, fileName, relative);
    }

    public void Dispose()
    {
        _workspace.Changed -= OnWorkspaceChanged;
        _cts.Cancel();
        _cts.Dispose();
    }
}
