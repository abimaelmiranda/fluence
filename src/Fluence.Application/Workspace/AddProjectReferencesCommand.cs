using System.Collections.Generic;

namespace Fluence.Application.Workspace;

public sealed record AddProjectReferencesCommand(
    string ProjectPath,
    IReadOnlyList<string> ReferencedProjectPaths);
