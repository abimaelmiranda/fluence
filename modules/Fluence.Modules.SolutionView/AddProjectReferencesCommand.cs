using System.Collections.Generic;

namespace Fluence.Modules.SolutionView;

public sealed record AddProjectReferencesCommand(
    string ProjectPath,
    IReadOnlyList<string> ReferencedProjectPaths);
