using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Storage;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;

namespace Fluence.Infrastructure;

public sealed class WorkspaceSnapshotService(IFluenceStorageService storage) : IWorkspaceSnapshotService
{
    public Task<WorkspaceSnapshot?> LoadAsync(string workspaceRoot, CancellationToken cancellationToken = default) =>
        storage.ReadProjectAsync(workspaceRoot, "snapshot.json", WorkspaceSnapshotJsonContext.Default.WorkspaceSnapshot, cancellationToken);

    public Task SaveAsync(string workspaceRoot, WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default) =>
        storage.WriteProjectAsync(workspaceRoot, "snapshot.json", snapshot, WorkspaceSnapshotJsonContext.Default.WorkspaceSnapshot, cancellationToken);
}
