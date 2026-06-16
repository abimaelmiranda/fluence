namespace Fluence.Modules.SourceControl.Models;

public record GitBranch(
    string Name,
    bool IsCurrent,
    bool IsLocal,
    bool IsRemote
);
