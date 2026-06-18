using System;
using System.Collections.Generic;
using System.Linq;
using Fluence.Core.Abstractions.Problems;
using Fluence.Core.Models.Problems;

namespace Fluence.Core.Services.Problems;

public sealed class ProblemService : IProblemService
{
    private readonly object _gate = new();
    private readonly List<ProblemItem> _problems = [];

    public event EventHandler? Changed;

    public IReadOnlyList<ProblemItem> GetProblems()
    {
        lock (_gate)
        {
            return Sort(_problems).ToArray();
        }
    }

    public IReadOnlyList<ProblemItem> GetVisibleProblems(
        string? activeFilePath,
        bool showWarningsFromAllFiles,
        bool showInformationFromAllFiles,
        int maxCount)
    {
        var limit = Math.Clamp(maxCount, 1, 5000);
        lock (_gate)
        {
            return Sort(_problems.Where(problem => IsVisible(
                    problem,
                    activeFilePath,
                    showWarningsFromAllFiles,
                    showInformationFromAllFiles)))
                .Take(limit)
                .ToArray();
        }
    }

    public void ReplaceSource(string source, IReadOnlyList<ProblemItem> problems)
    {
        lock (_gate)
        {
            _problems.RemoveAll(problem => string.Equals(problem.Source, source, StringComparison.Ordinal));
            _problems.AddRange(problems);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceFile(string source, string filePath, IReadOnlyList<ProblemItem> problems)
    {
        lock (_gate)
        {
            _problems.RemoveAll(problem =>
                string.Equals(problem.Source, source, StringComparison.Ordinal) &&
                string.Equals(problem.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            _problems.AddRange(problems);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSource(string source)
    {
        lock (_gate)
        {
            if (_problems.RemoveAll(problem => string.Equals(problem.Source, source, StringComparison.Ordinal)) == 0)
                return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsVisible(
        ProblemItem problem,
        string? activeFilePath,
        bool showWarningsFromAllFiles,
        bool showInformationFromAllFiles)
    {
        if (problem.Severity == ProblemSeverity.Error)
            return true;

        var isActiveFile = !string.IsNullOrWhiteSpace(activeFilePath) &&
                           string.Equals(problem.FilePath, activeFilePath, StringComparison.OrdinalIgnoreCase);
        return problem.Severity switch
        {
            ProblemSeverity.Warning => showWarningsFromAllFiles || isActiveFile,
            ProblemSeverity.Information => showInformationFromAllFiles || isActiveFile,
            ProblemSeverity.Hint => isActiveFile,
            _ => isActiveFile,
        };
    }

    private static IOrderedEnumerable<ProblemItem> Sort(IEnumerable<ProblemItem> problems) =>
        problems
            .OrderBy(problem => problem.Severity)
            .ThenBy(problem => problem.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(problem => problem.Line)
            .ThenBy(problem => problem.Character);
}
