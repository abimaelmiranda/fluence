using System.Collections.Generic;

namespace Fluence.Modules.SourceControl.Models;

public sealed record GitStatus(
    string Branch,
    IReadOnlyList<GitFileChange> StagedChanges,
    IReadOnlyList<GitFileChange> UnstagedChanges);
