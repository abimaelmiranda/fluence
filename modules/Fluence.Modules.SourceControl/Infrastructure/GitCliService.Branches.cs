using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.Infrastructure;

public sealed partial class GitCliService
{
    public async Task<IReadOnlyList<GitBranch>> GetBranchesAsync(string repoRoot)
    {
        try
        {
            var output = await RunGitAsync(repoRoot, "branch", "-a");
            var branches = new List<GitBranch>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length < 2) continue;
                var isCurrent = line[0] == '*';
                var name = line[2..].Trim();

                if (name.StartsWith("(HEAD", StringComparison.Ordinal)) continue;
                if (name.Contains("HEAD ->", StringComparison.Ordinal)) continue;

                var isRemote = name.StartsWith("remotes/", StringComparison.Ordinal);
                if (isRemote)
                    name = name["remotes/".Length..];

                if (!seen.Add(name)) continue;

                branches.Add(new GitBranch(name, isCurrent, !isRemote, isRemote));
            }

            return branches
                .OrderBy(b => b.IsCurrent ? 0 : b.IsLocal ? 1 : 2)
                .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Git branches lookup failed: {ex}");
            return Array.Empty<GitBranch>();
        }
    }

    public async Task<CheckoutResult> CheckoutBranchAsync(string branchName, string repoRoot)
    {
        var (output, exitCode) = await RunGitWithExitCodeAsync(repoRoot, "checkout", branchName);
        if (exitCode == 0) return CheckoutResult.Success;
        if (output.Contains("would be overwritten by checkout", StringComparison.OrdinalIgnoreCase))
            return CheckoutResult.HasLocalChanges;
        return CheckoutResult.Error;
    }

    public async Task<CheckoutResult> ForceCheckoutBranchAsync(string branchName, string repoRoot)
    {
        var (_, exitCode) = await RunGitWithExitCodeAsync(repoRoot, "checkout", "-f", branchName);
        return exitCode == 0 ? CheckoutResult.Success : CheckoutResult.Error;
    }

    public async Task DeleteBranchAsync(string branchName, bool force, string repoRoot)
    {
        var flag = force ? "-D" : "-d";
        await RunGitAsync(repoRoot, "branch", flag, branchName);
    }
}
