using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Abstractions.Dialogs;

public interface ILaunchSetupDialogService
{
    Task<string?> SelectStartupProjectAsync(
        string workspaceRoot,
        IReadOnlyList<string> projectPaths,
        CancellationToken cancellationToken = default);

    Task<string?> SelectLaunchProfileAsync(
        string workspaceRoot,
        string projectPath,
        ExecutionMode mode,
        IReadOnlyList<DotnetLaunchProfile> profiles,
        CancellationToken cancellationToken = default);
}
