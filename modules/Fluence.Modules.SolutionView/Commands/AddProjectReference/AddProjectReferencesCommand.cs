using System.Collections.Generic;

namespace Fluence.Modules.SolutionView.Commands.AddProjectReference;

public sealed record AddProjectReferencesCommand(
    string ProjectPath,
    IReadOnlyList<string> ReferencedProjectPaths);