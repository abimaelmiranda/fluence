namespace Fluence.Core.Models.Workspace;

public sealed class WorkspaceSnapshot
{
    public string[] OpenTabs { get; init; } = [];
    public string? ActiveTabPath { get; init; }
}
