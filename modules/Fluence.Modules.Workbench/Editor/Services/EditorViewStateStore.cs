using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Storage;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Modules.Workbench.Editor.Json;

namespace Fluence.Modules.Workbench.Editor.Services;

public sealed class EditorViewStateStore(
    IWorkspaceContext workspace,
    IFluenceStorageService storage)
{
    private const string FileName = "editor-view-state.json";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, EditorDocumentViewState> _documents = new(StringComparer.OrdinalIgnoreCase);
    private string? _loadedWorkspaceRoot;

    public async Task<EditorDocumentViewState?> GetAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return _documents.TryGetValue(filePath, out var state)
                ? Clone(state)
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(EditorDocumentViewState state, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(state.FilePath))
            return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            _documents[state.FilePath] = Clone(state);
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        var root = ResolveWorkspaceRoot();
        if (string.Equals(root, _loadedWorkspaceRoot, StringComparison.OrdinalIgnoreCase))
            return;

        _documents.Clear();
        _loadedWorkspaceRoot = root;
        if (root is null)
            return;

        var cache = await storage
            .ReadProjectAsync(root, FileName, EditorViewStateJsonContext.Default.EditorViewStateCache, cancellationToken)
            .ConfigureAwait(false);

        if (cache?.Documents is null)
            return;

        foreach (var document in cache.Documents.Where(d => !string.IsNullOrWhiteSpace(d.FilePath)))
            _documents[document.FilePath] = Clone(document);
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var root = _loadedWorkspaceRoot;
        if (root is null)
            return;

        var cache = new EditorViewStateCache
        {
            Documents = _documents.Values
                .OrderBy(d => d.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(Clone)
                .ToList(),
        };

        await storage
            .WriteProjectAsync(root, FileName, cache, EditorViewStateJsonContext.Default.EditorViewStateCache, cancellationToken)
            .ConfigureAwait(false);
    }

    private string? ResolveWorkspaceRoot()
    {
        var current = workspace.Current;
        return current.NavigationMode switch
        {
            WorkspaceMode.Folder when !string.IsNullOrWhiteSpace(current.CurrentFolderPath) =>
                current.CurrentFolderPath,
            WorkspaceMode.Solution when !string.IsNullOrWhiteSpace(current.CurrentSolutionPath) =>
                Path.GetDirectoryName(current.CurrentSolutionPath),
            _ => null,
        };
    }

    private static EditorDocumentViewState Clone(EditorDocumentViewState state) =>
        new()
        {
            FilePath = state.FilePath,
            CaretOffset = state.CaretOffset,
            ScrollX = state.ScrollX,
            ScrollY = state.ScrollY,
        };
}
