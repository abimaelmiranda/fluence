using System.IO;
using Fluence.Core.Models.Problems;

namespace Fluence.Desktop.ViewModels;

public sealed class ProblemItemViewModel(ProblemItem problem)
{
    public ProblemItem Problem { get; } = problem;

    public string Severity => problem.Severity.ToString();

    public bool IsError => problem.Severity == ProblemSeverity.Error;

    public bool IsWarning => problem.Severity == ProblemSeverity.Warning;

    public bool IsInformation => problem.Severity == ProblemSeverity.Information;

    public bool IsHint => problem.Severity == ProblemSeverity.Hint;

    public string FileName => Path.GetFileName(problem.FilePath);

    public string Location => $"{problem.Line + 1}:{problem.Character + 1}";

    public string Source => string.IsNullOrWhiteSpace(problem.Code)
        ? problem.Source
        : $"{problem.Source} {problem.Code}";

    public string Message => problem.Message;
}
