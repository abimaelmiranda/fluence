using System.Collections.Generic;

namespace Fluence.Modules.Workbench.SolutionView.Commands.AddProjectReference;

public sealed record AddProjectReferencesCommand(
    string ProjectPath,
    IReadOnlyList<string> ReferencedProjectPaths);