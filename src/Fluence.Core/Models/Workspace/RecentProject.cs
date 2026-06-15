using System;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Models.Workspace;

public sealed class RecentProject
{
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public RecentProjectKind Kind { get; init; }
    public DateTimeOffset LastOpened { get; init; }
}

public sealed class RecentProjectsData
{
    public List<RecentProject> Recents { get; init; } = [];
}
