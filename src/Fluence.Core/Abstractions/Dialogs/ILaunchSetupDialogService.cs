namespace Fluence.Core.Abstractions.Dialogs;

public interface ILaunchSetupDialogService
{
    Task<string?> SelectStartupProjectAsync(
        string workspaceRoot,
        IReadOnlyList<string> projectPaths,
        CancellationToken cancellationToken = default);
}
