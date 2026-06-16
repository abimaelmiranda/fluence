using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.Infrastructure;

public sealed partial class GitCliService
{
    public async Task<GitStatus> GetStatusAsync(string repoRoot)
    {
        var branch = await GetCurrentBranchAsync(repoRoot);
        var output = await RunGitAsync(repoRoot, "status", "--porcelain=v1", "-u");

        var staged = new List<GitFileChange>();
        var unstaged = new List<GitFileChange>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 3) continue;

            var stagedStatus = line[0];
            var unstagedStatus = line[1];
            var pathData = ParsePathData(line[3..], stagedStatus);

            if (stagedStatus != ' ' && stagedStatus != '?')
                staged.Add(new GitFileChange(pathData.FilePath, ParseStatus(stagedStatus), IsStaged: true, pathData.OriginalPath));

            if (unstagedStatus != ' ' && unstagedStatus != '?')
                unstaged.Add(new GitFileChange(pathData.FilePath, ParseStatus(unstagedStatus), IsStaged: false, pathData.OriginalPath));
            else if (stagedStatus == '?' && unstagedStatus == '?')
                unstaged.Add(new GitFileChange(pathData.FilePath, GitChangeStatus.Untracked, IsStaged: false));
        }

        return new GitStatus(branch, staged, unstaged);
    }

    public async Task<string> GetCurrentBranchAsync(string repoRoot)
    {
        try
        {
            return await RunGitAsync(repoRoot, "branch", "--show-current");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Git current branch lookup failed: {ex}");
            return "unknown";
        }
    }

    private static (string FilePath, string? OriginalPath) ParsePathData(string rawPath, char stagedStatus)
    {
        rawPath = rawPath.Trim();
        var isRenameOrCopy = stagedStatus is 'R' or 'C';
        var separatorIdx = isRenameOrCopy ? rawPath.IndexOf(" -> ", StringComparison.Ordinal) : -1;

        if (separatorIdx < 0)
            return (UnquotePath(rawPath), null);

        var originalPath = UnquotePath(rawPath[..separatorIdx]);
        var filePath = UnquotePath(rawPath[(separatorIdx + 4)..]);
        return (filePath, originalPath);
    }

    private static string UnquotePath(string path)
    {
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
            path = UnescapeCString(path[1..^1]);
        return path;
    }

    private static string UnescapeCString(string s)
    {
        var sb = new StringBuilder(s.Length);
        var i = 0;

        while (i < s.Length)
        {
            if (s[i] != '\\' || i + 1 >= s.Length)
            {
                sb.Append(s[i++]);
                continue;
            }

            i++;
            switch (s[i])
            {
                case 'n': sb.Append('\n'); i++; break;
                case 't': sb.Append('\t'); i++; break;
                case 'r': sb.Append('\r'); i++; break;
                case '"': sb.Append('"'); i++; break;
                case '\\': sb.Append('\\'); i++; break;
                default:
                    i = AppendOctalOrBackslash(s, i, sb);
                    break;
            }
        }

        return sb.ToString();
    }

    private static int AppendOctalOrBackslash(string s, int i, StringBuilder sb)
    {
        if (i + 2 >= s.Length || !IsOctal(s[i]) || !IsOctal(s[i + 1]) || !IsOctal(s[i + 2]))
        {
            sb.Append('\\');
            return i;
        }

        var bytes = new List<byte>();
        while (i + 2 < s.Length && IsOctal(s[i]) && IsOctal(s[i + 1]) && IsOctal(s[i + 2]))
        {
            bytes.Add((byte)((s[i] - '0') * 64 + (s[i + 1] - '0') * 8 + (s[i + 2] - '0')));
            i += 3;
            if (i < s.Length && s[i] == '\\' && i + 1 < s.Length)
                i++;
        }

        sb.Append(Encoding.UTF8.GetString(bytes.ToArray()));
        return i;
    }

    private static bool IsOctal(char c) => c >= '0' && c <= '7';
}
