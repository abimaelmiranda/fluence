namespace Fluence.Core.Abstractions.Workspace;

public interface IProjectAssociationService
{
    string? FindProjectForFile(string solutionPath, string filePath);
}
