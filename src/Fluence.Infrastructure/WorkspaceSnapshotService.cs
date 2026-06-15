using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;

namespace Fluence.Infrastructure;

public sealed class WorkspaceSnapshotService : IWorkspaceSnapshotService
{
    private static string GetSnapshotPath(string workspaceRoot) =>
        Path.Combine(workspaceRoot, ".fluence", "snapshot.json");

    public async Task<WorkspaceSnapshot?> LoadAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var snapshotPath = GetSnapshotPath(workspaceRoot);
        if (!File.Exists(snapshotPath))
            return null;

        try
        {
            await using var stream = File.OpenRead(snapshotPath);
            return await JsonSerializer.DeserializeAsync(
                stream,
                WorkspaceSnapshotJsonContext.Default.WorkspaceSnapshot,
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(string workspaceRoot, WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var snapshotPath = GetSnapshotPath(workspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);

        await using var stream = File.Create(snapshotPath);
        await JsonSerializer.SerializeAsync(
            stream,
            snapshot,
            WorkspaceSnapshotJsonContext.Default.WorkspaceSnapshot,
            cancellationToken);
    }
}
