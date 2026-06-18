using System;
using System.IO;
using System.Linq;
using System.Threading;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using WorkspaceEntity = Fluence.Core.Models.Workspace.Workspace;

namespace Fluence.Infrastructure;

public sealed class WorkspaceSnapshotCoordinator : IDisposable
{
    private const int DebounceMs = 1500;

    private readonly IWorkspaceContext _workspace;
    private readonly IWorkspaceSnapshotService _snapshots;
    private Timer? _debounceTimer;

    public WorkspaceSnapshotCoordinator(IWorkspaceContext workspace, IWorkspaceSnapshotService snapshots)
    {
        _workspace = workspace;
        _snapshots = snapshots;
        _workspace.Changed += OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        var ws = _workspace.Current;
        if (ws.Mode is not (WorkspaceMode.Folder or WorkspaceMode.Solution))
        {
            _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        _debounceTimer ??= new Timer(SaveSnapshot, null, Timeout.Infinite, Timeout.Infinite);
        _debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    private void SaveSnapshot(object? state)
    {
        var ws = _workspace.Current;
        var root = ResolveRoot(ws);
        if (root is null) return;

        var snapshot = BuildSnapshot(ws);
        _ = _snapshots.SaveAsync(root, snapshot);
    }

    public bool HasWorkspaceToSave =>
        _workspace.Current.Mode is WorkspaceMode.Folder or WorkspaceMode.Solution;

    public Task SaveAsync()
    {
        _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);

        var ws = _workspace.Current;
        if (ws.Mode is not (WorkspaceMode.Folder or WorkspaceMode.Solution))
            return Task.CompletedTask;

        var root = ResolveRoot(ws);
        if (root is null) return Task.CompletedTask;

        return _snapshots.SaveAsync(root, BuildSnapshot(ws));
    }

    private static WorkspaceSnapshot BuildSnapshot(WorkspaceEntity ws) =>
        new()
        {
            OpenTabs = ws.TabSession.Documents
                .Where(d => d.Kind == OpenDocumentKind.TextDocument)
                .Select(d => d.Path)
                .ToArray(),
            ActiveTabPath = ws.TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument
                ? ws.TabSession.ActiveDocument.Path
                : null,
        };

    private static string? ResolveRoot(WorkspaceEntity ws) => ws.Mode switch
    {
        WorkspaceMode.Folder => ws.CurrentFolderPath,
        WorkspaceMode.Solution => ws.CurrentSolutionPath is not null
            ? Path.GetDirectoryName(ws.CurrentSolutionPath)
            : null,
        _ => null,
    };

    public void Dispose()
    {
        _workspace.Changed -= OnChanged;
        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }
}
