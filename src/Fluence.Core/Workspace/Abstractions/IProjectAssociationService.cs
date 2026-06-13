namespace Fluence.Core.Workspace;

public interface IProjectAssociationService
{
    string? FindProjectForFile(string solutionPath, string filePath);
}
