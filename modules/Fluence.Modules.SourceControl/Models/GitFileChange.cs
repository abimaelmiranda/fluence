namespace Fluence.Modules.SourceControl.Models;

public sealed record GitFileChange(string FilePath, GitChangeStatus Status, bool IsStaged, string? OriginalPath = null)
{
    public string FileName => System.IO.Path.GetFileName(FilePath.TrimEnd('/', '\\'));

    public bool DeletesUntrackedFile => Status is GitChangeStatus.Untracked || (Status is GitChangeStatus.Added && IsStaged);

    public string StatusLabel => Status switch
    {
        GitChangeStatus.Modified => "M",
        GitChangeStatus.Added => "A",
        GitChangeStatus.Deleted => "D",
        GitChangeStatus.Renamed => "R",
        GitChangeStatus.Untracked => "U",
        GitChangeStatus.Conflicted => "C",
        _ => "?",
    };

    public string StatusColor => Status switch
    {
        GitChangeStatus.Modified   => "#d29922", // yellow
        GitChangeStatus.Added      => "#3fb950", // green
        GitChangeStatus.Deleted    => "#f85149", // red
        GitChangeStatus.Renamed    => "#79c0ff", // blue
        GitChangeStatus.Untracked  => "#3fb950", // green
        GitChangeStatus.Conflicted => "#ff7b72", // orange-red
        _                          => "#8b949e",
    };
}
