using System.Threading.Tasks;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.Infrastructure;

public sealed partial class GitCliService
{
    public async Task StageAsync(string filePath, string repoRoot)
        => await RunGitAsync(repoRoot, "add", "--", NormalizePath(filePath));

    public async Task UnstageAsync(string filePath, string repoRoot)
        => await RunGitAsync(repoRoot, "restore", "--staged", "--", NormalizePath(filePath));

    public async Task RevertFileAsync(GitFileChange change, string repoRoot)
    {
        if (change.Status == GitChangeStatus.Renamed && change.OriginalPath is not null)
        {
            await RevertRenameAsync(change, repoRoot);
            return;
        }

        if (change.DeletesUntrackedFile)
        {
            if (change.IsStaged)
                await RunGitAsync(repoRoot, "restore", "--staged", "--", NormalizePath(change.FilePath));

            await RunGitAsync(repoRoot, "clean", "-fd", "--", NormalizePath(change.FilePath));
            return;
        }

        if (change.IsStaged)
        {
            await RunGitAsync(
                repoRoot,
                "restore",
                "--source=HEAD",
                "--staged",
                "--worktree",
                "--",
                NormalizePath(change.FilePath));
            return;
        }

        await RunGitAsync(repoRoot, "restore", "--source=HEAD", "--worktree", "--", NormalizePath(change.FilePath));
    }

    public async Task CommitAsync(string message, string repoRoot)
        => await RunGitAsync(repoRoot, "commit", "-m", message);

    public async Task<string> GetDiffAsync(string filePath, bool staged, string repoRoot)
    {
        return staged
            ? await RunGitAsync(repoRoot, "diff", "--cached", "--", NormalizePath(filePath))
            : await RunGitAsync(repoRoot, "diff", "--", NormalizePath(filePath));
    }

    private static async Task RevertRenameAsync(GitFileChange change, string repoRoot)
    {
        var currentPath = NormalizePath(change.FilePath);
        var originalPath = NormalizePath(change.OriginalPath!);

        await RunGitAsync(repoRoot, "restore", "--staged", "--", originalPath, currentPath);
        await RunGitAsync(repoRoot, "restore", "--source=HEAD", "--worktree", "--", originalPath);
        await RunGitAsync(repoRoot, "clean", "-fd", "--", currentPath);
    }

    private static string NormalizePath(string filePath) => filePath.TrimEnd('/', '\\');
}
