using System.Collections.Generic;
using System.Threading.Tasks;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.Abstractions;

public interface IGitService
{
    Task<string?> GetRepositoryRootAsync(string workingDirectory);
    Task<GitStatus> GetStatusAsync(string repoRoot);
    Task StageAsync(string filePath, string repoRoot);
    Task UnstageAsync(string filePath, string repoRoot);
    Task RevertFileAsync(GitFileChange change, string repoRoot);
    Task CommitAsync(string message, string repoRoot);
    Task<string> GetDiffAsync(string filePath, bool staged, string repoRoot);
    Task<string> GetCurrentBranchAsync(string repoRoot);
    Task<string> PullAsync(string repoRoot);
    Task<string> PushAsync(string repoRoot);
    Task<IReadOnlyList<GitBranch>> GetBranchesAsync(string repoRoot);
    Task<CheckoutResult> CheckoutBranchAsync(string branchName, string repoRoot);
    Task<CheckoutResult> ForceCheckoutBranchAsync(string branchName, string repoRoot);
    Task StashAsync(string repoRoot);
    Task DeleteBranchAsync(string branchName, bool force, string repoRoot);
    Task FetchAsync(string repoRoot);
    Task<(int Ahead, int Behind)> GetAheadBehindAsync(string repoRoot);
    Task<IReadOnlyList<GitStash>> GetStashListAsync(string repoRoot);
    Task PopStashAsync(string stashRef, string repoRoot);
    Task DropStashAsync(string stashRef, string repoRoot);
}
