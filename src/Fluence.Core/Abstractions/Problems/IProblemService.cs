using System;
using System.Collections.Generic;
using Fluence.Core.Models.Problems;

namespace Fluence.Core.Abstractions.Problems;

public interface IProblemService
{
    event EventHandler? Changed;

    IReadOnlyList<ProblemItem> GetProblems();

    IReadOnlyList<ProblemItem> GetVisibleProblems(
        string? activeFilePath,
        bool showWarningsFromAllFiles,
        bool showInformationFromAllFiles,
        int maxCount);

    void ReplaceSource(string source, IReadOnlyList<ProblemItem> problems);

    void ReplaceFile(string source, string filePath, IReadOnlyList<ProblemItem> problems);

    void ClearFile(string source, string filePath);

    void ClearSource(string source);
}
