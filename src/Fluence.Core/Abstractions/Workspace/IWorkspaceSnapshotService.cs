using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.Workspace;

namespace Fluence.Core.Abstractions.Workspace;

public interface IWorkspaceSnapshotService
{
    Task<WorkspaceSnapshot?> LoadAsync(string workspaceRoot, CancellationToken cancellationToken = default);
    Task SaveAsync(string workspaceRoot, WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default);
}
