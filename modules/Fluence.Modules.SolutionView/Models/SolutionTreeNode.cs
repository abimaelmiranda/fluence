using System.Collections.Generic;
using Fluence.Modules.SolutionView.Models.Enums;

namespace Fluence.Modules.SolutionView.Models;

public sealed record SolutionTreeNode(
    SolutionTreeNodeKind Kind,
    string Name,
    string? Path,
    IReadOnlyList<SolutionTreeNode> Children,
    string? Version = null,
    string? ProjectPath = null,
    string? ReferencedProjectPath = null,
    bool IsResolved = true);