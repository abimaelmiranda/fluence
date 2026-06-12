using System.Collections.Generic;

namespace Fluence.Application.Workspace;

public sealed record SolutionTreeNode(
    SolutionTreeNodeKind Kind,
    string Name,
    string? Path,
    IReadOnlyList<SolutionTreeNode> Children,
    string? Version = null,
    string? ProjectPath = null,
    string? ReferencedProjectPath = null,
    bool IsResolved = true);
