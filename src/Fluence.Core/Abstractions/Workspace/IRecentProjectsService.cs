using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Abstractions.Workspace;

public interface IRecentProjectsService
{
    IReadOnlyList<RecentProject> GetRecents();
    Task AddAsync(string path, RecentProjectKind kind, CancellationToken cancellationToken = default);
}
