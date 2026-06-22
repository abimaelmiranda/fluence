using System;
using System.IO;
using System.Text.RegularExpressions;
using Fluence.Core.Models.Problems;

namespace Fluence.Core.Services.Problems;

public static partial class MsBuildProblemParser
{
    public static ProblemItem? TryParse(string line, string? workingDirectory)
    {
        // Format with source location: path(line,col): severity code: message
        var match = BuildProblemRegex().Match(line);
        if (match.Success)
        {
            var lineNumber = ParseOneBased(match.Groups["line"].Value);
            var columnNumber = ParseOneBased(match.Groups["column"].Value);
            return CreateItem(match, workingDirectory, Math.Max(0, lineNumber - 1), Math.Max(0, columnNumber - 1));
        }

        // Format without location (NuGet/MSBuild): path : severity code: message [context]
        match = BuildProblemNoLocationRegex().Match(line);
        if (match.Success)
            return CreateItem(match, workingDirectory, 0, 0);

        return null;
    }

    private static ProblemItem CreateItem(Match match, string? workingDirectory, int line, int character)
    {
        var severityText = match.Groups["severity"].Value;
        var severity = string.Equals(severityText, "error", StringComparison.OrdinalIgnoreCase)
            ? ProblemSeverity.Error
            : ProblemSeverity.Warning;

        var filePath = match.Groups["path"].Value.Trim();
        if (!Path.IsPathRooted(filePath) && !string.IsNullOrWhiteSpace(workingDirectory))
            filePath = Path.GetFullPath(Path.Combine(workingDirectory, filePath));

        return new ProblemItem(
            FilePath: filePath,
            Line: line,
            Character: character,
            Severity: severity,
            Source: ProblemSourceIds.Build,
            Code: match.Groups["code"].Value.Trim(),
            Message: match.Groups["message"].Value.Trim());
    }

    private static int ParseOneBased(string value) =>
        int.TryParse(value, out var parsed) ? parsed : 1;

    // path(line,col): severity code: message
    [GeneratedRegex(@"^\s*(?<path>.+?)\((?<line>\d+),(?<column>\d+)\):\s*(?<severity>error|warning)\s+(?<code>[A-Z]+\d+)\s*:\s*(?<message>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex BuildProblemRegex();

    // path : severity code: message  [context]
    [GeneratedRegex(@"^\s*(?<path>.+?)\s+:\s+(?<severity>error|warning)\s+(?<code>[A-Z]+\d+)\s*:\s*(?<message>.+?)(\s+\[.+\])?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex BuildProblemNoLocationRegex();
}
