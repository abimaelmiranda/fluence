using System;
using System.IO;
using System.Text.RegularExpressions;
using Fluence.Core.Models.Problems;

namespace Fluence.Modules.DotnetCli.Services;

public static partial class BuildProblemParser
{
    private const string Source = "Build";

    public static ProblemItem? TryParse(string line, string? workingDirectory)
    {
        var match = BuildProblemRegex().Match(line);
        if (!match.Success)
            return null;

        var severityText = match.Groups["severity"].Value;
        var severity = string.Equals(severityText, "error", StringComparison.OrdinalIgnoreCase)
            ? ProblemSeverity.Error
            : ProblemSeverity.Warning;

        var filePath = match.Groups["path"].Value.Trim();
        if (!Path.IsPathRooted(filePath) && !string.IsNullOrWhiteSpace(workingDirectory))
            filePath = Path.GetFullPath(Path.Combine(workingDirectory, filePath));

        var lineNumber = ParseOneBased(match.Groups["line"].Value);
        var columnNumber = ParseOneBased(match.Groups["column"].Value);
        return new ProblemItem(
            FilePath: filePath,
            Line: Math.Max(0, lineNumber - 1),
            Character: Math.Max(0, columnNumber - 1),
            Severity: severity,
            Source: Source,
            Code: match.Groups["code"].Value.Trim(),
            Message: match.Groups["message"].Value.Trim());
    }

    private static int ParseOneBased(string value) =>
        int.TryParse(value, out var parsed) ? parsed : 1;

    [GeneratedRegex(@"^\s*(?<path>.+?)\((?<line>\d+),(?<column>\d+)\):\s*(?<severity>error|warning)\s+(?<code>[A-Z]+\d+)\s*:\s*(?<message>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex BuildProblemRegex();
}
