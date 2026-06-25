using Fluence.Modules.Workbench.SolutionView.Models.Enums;

namespace Fluence.Modules.Workbench.SolutionView.Models;

public sealed record SolutionFileCreationRequest(string Name, SolutionFileKind Kind);