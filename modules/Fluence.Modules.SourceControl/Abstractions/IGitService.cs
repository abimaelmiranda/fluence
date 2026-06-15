using System.Threading.Tasks;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.Abstractions;

public interface IGitService
{
    Task<string?> GetRepositoryRootAsync(string workingDirectory);
    Task<GitStatus> GetStatusAsync(string repoRoot);
    Task StageAsync(string filePath, string repoRoot);
    Task UnstageAsync(string filePath, string repoRoot);
    Task CommitAsync(string message, string repoRoot);
    Task<string> GetDiffAsync(string filePath, bool staged, string repoRoot);
    Task<string> GetCurrentBranchAsync(string repoRoot);
    Task<string> PullAsync(string repoRoot);
    Task<string> PushAsync(string repoRoot);
}
