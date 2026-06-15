using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Fluence.Modules.SourceControl.Abstractions;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.Infrastructure;

public sealed class GitCliService : IGitService
{
    public async Task<string?> GetRepositoryRootAsync(string workingDirectory)
    {
        try
        {
            var result = await RunGitAsync("rev-parse --show-toplevel", workingDirectory);
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch
        {
            return null;
        }
    }

    public async Task<GitStatus> GetStatusAsync(string repoRoot)
    {
        var branch = await GetCurrentBranchAsync(repoRoot);
        var output = await RunGitAsync("status --porcelain=v1 -u", repoRoot);

        var staged = new List<GitFileChange>();
        var unstaged = new List<GitFileChange>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 3) continue;

            char x = line[0]; // staged status
            char y = line[1]; // unstaged status
            var rawPath = line[3..].Trim();
            // renames/copies: "orig -> new" — extract only when status IS rename/copy
            var isRenameOrCopy = x == 'R' || x == 'C';
            var separatorIdx = isRenameOrCopy ? rawPath.IndexOf(" -> ", StringComparison.Ordinal) : -1;
            var path = separatorIdx >= 0 ? rawPath[(separatorIdx + 4)..] : rawPath;
            // git quotes paths with C-style escaping when core.quotePath=true
            path = UnquotePath(path);

            if (x != ' ' && x != '?')
                staged.Add(new GitFileChange(path, ParseStatus(x), IsStaged: true));

            if (y != ' ' && y != '?')
                unstaged.Add(new GitFileChange(path, ParseStatus(y), IsStaged: false));
            else if (x == '?' && y == '?')
                unstaged.Add(new GitFileChange(path, GitChangeStatus.Untracked, IsStaged: false));
        }

        return new GitStatus(branch, staged, unstaged);
    }

    public async Task StageAsync(string filePath, string repoRoot)
        => await RunGitAsync($"add -- \"{filePath.TrimEnd('/', '\\')}\"", repoRoot);

    public async Task UnstageAsync(string filePath, string repoRoot)
        => await RunGitAsync($"restore --staged -- \"{filePath.TrimEnd('/', '\\')}\"", repoRoot);

    public async Task CommitAsync(string message, string repoRoot)
        => await RunGitAsync($"commit -m \"{EscapeArg(message)}\"", repoRoot);

    public async Task<string> GetDiffAsync(string filePath, bool staged, string repoRoot)
    {
        var args = staged
            ? $"diff --cached -- \"{filePath}\""
            : $"diff -- \"{filePath}\"";
        return await RunGitAsync(args, repoRoot);
    }

    public async Task<string> GetCurrentBranchAsync(string repoRoot)
    {
        try
        {
            return await RunGitAsync("branch --show-current", repoRoot);
        }
        catch
        {
            return "unknown";
        }
    }

    public async Task<string> PullAsync(string repoRoot)
        => await RunGitWithStderrAsync("pull", repoRoot);

    public async Task<string> PushAsync(string repoRoot)
        => await RunGitWithStderrAsync("push", repoRoot);

    private static async Task<string> RunGitAsync(string args, string workingDirectory)
    {
        using var process = CreateProcess(args, workingDirectory);
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output.TrimEnd(); // TrimEnd only — Trim() strips leading spaces that are meaningful in porcelain format
    }

    private static async Task<string> RunGitWithStderrAsync(string args, string workingDirectory)
    {
        using var process = CreateProcess(args, workingDirectory);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var combined = (await stdout) + (await stderr);
        return combined.Trim();
    }

    private static Process CreateProcess(string args, string workingDirectory) => new()
    {
        StartInfo = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }
    };

    private static GitChangeStatus ParseStatus(char c) => c switch
    {
        'M' => GitChangeStatus.Modified,
        'A' => GitChangeStatus.Added,
        'D' => GitChangeStatus.Deleted,
        'R' => GitChangeStatus.Renamed,
        'U' => GitChangeStatus.Conflicted,
        _ => GitChangeStatus.Modified,
    };

    private static string EscapeArg(string value) => value.Replace("\"", "\\\"");

    private static string UnquotePath(string path)
    {
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
            path = UnescapeCString(path[1..^1]);
        return path;
    }

    private static string UnescapeCString(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] != '\\' || i + 1 >= s.Length)
            {
                sb.Append(s[i++]);
                continue;
            }
            i++; // skip backslash
            switch (s[i])
            {
                case 'n':  sb.Append('\n'); i++; break;
                case 't':  sb.Append('\t'); i++; break;
                case 'r':  sb.Append('\r'); i++; break;
                case '"':  sb.Append('"');  i++; break;
                case '\\': sb.Append('\\'); i++; break;
                default:
                    // octal sequence \NNN — git uses multi-byte UTF-8 as consecutive \NNN\NNN...
                    if (i + 2 < s.Length && IsOctal(s[i]) && IsOctal(s[i + 1]) && IsOctal(s[i + 2]))
                    {
                        var bytes = new System.Collections.Generic.List<byte>();
                        while (i + 2 < s.Length && IsOctal(s[i]) && IsOctal(s[i + 1]) && IsOctal(s[i + 2]))
                        {
                            bytes.Add((byte)((s[i] - '0') * 64 + (s[i + 1] - '0') * 8 + (s[i + 2] - '0')));
                            i += 3;
                            if (i < s.Length && s[i] == '\\' && i + 1 < s.Length) i++; // skip next backslash if octal continues
                        }
                        sb.Append(System.Text.Encoding.UTF8.GetString(bytes.ToArray()));
                    }
                    else
                    {
                        sb.Append('\\');
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    private static bool IsOctal(char c) => c >= '0' && c <= '7';
}
