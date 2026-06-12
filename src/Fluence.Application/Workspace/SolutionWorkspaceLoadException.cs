using System;

namespace Fluence.Application.Workspace;

public sealed class SolutionWorkspaceLoadException : InvalidOperationException
{
    public SolutionWorkspaceLoadException(string solutionPath, string reason)
        : base($"Unable to load solution: {solutionPath}. {reason}")
    {
        SolutionPath = solutionPath;
        Reason = reason;
    }

    public SolutionWorkspaceLoadException(string solutionPath, string reason, Exception innerException)
        : base($"Unable to load solution: {solutionPath}. {reason}", innerException)
    {
        SolutionPath = solutionPath;
        Reason = reason;
    }

    public string SolutionPath { get; }

    public string Reason { get; }
}
