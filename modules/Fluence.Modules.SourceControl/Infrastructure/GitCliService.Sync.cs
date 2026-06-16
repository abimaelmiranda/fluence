using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.Infrastructure;

public sealed partial class GitCliService
{
    public async Task<string> PullAsync(string repoRoot)
        => await RunGitWithStderrAsync(repoRoot, "pull");

    public async Task<string> PushAsync(string repoRoot)
        => await RunGitWithStderrAsync(repoRoot, "push");

    public async Task FetchAsync(string repoRoot)
        => await RunGitWithStderrAsync(repoRoot, "fetch", "--all");

    public async Task<(int Ahead, int Behind)> GetAheadBehindAsync(string repoRoot)
    {
        try
        {
            var output = await RunGitAsync(repoRoot, "rev-list", "--count", "--left-right", "@{u}...HEAD");
            var parts = output.Split('\t');
            if (parts.Length == 2
                && int.TryParse(parts[0].Trim(), out var behind)
                && int.TryParse(parts[1].Trim(), out var ahead))
            {
                return (ahead, behind);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Git ahead/behind lookup failed: {ex}");
        }

        return (0, 0);
    }

    public async Task StashAsync(string repoRoot)
        => await RunGitAsync(repoRoot, "stash");

    public async Task<IReadOnlyList<GitStash>> GetStashListAsync(string repoRoot)
    {
        try
        {
            var output = await RunGitAsync(repoRoot, "stash", "list", "--format=%gd|%s|%cr");
            var stashes = new List<GitStash>();

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|', 3);
                if (parts.Length == 3)
                    stashes.Add(new GitStash(parts[0].Trim(), parts[1].Trim(), parts[2].Trim()));
            }

            return stashes;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Git stash lookup failed: {ex}");
            return Array.Empty<GitStash>();
        }
    }

    public async Task PopStashAsync(string stashRef, string repoRoot)
        => await RunGitWithStderrAsync(repoRoot, "stash", "pop", stashRef);

    public async Task DropStashAsync(string stashRef, string repoRoot)
        => await RunGitAsync(repoRoot, "stash", "drop", stashRef);
}
