using Fluence.Core.Workspace;

namespace Fluence.Core.Ports;

public interface ILaunchSetupDialogService
{
    Task<string?> SelectStartupProjectAsync(
        string workspaceRoot,
        IReadOnlyList<string> projectPaths,
        CancellationToken cancellationToken = default);
}
