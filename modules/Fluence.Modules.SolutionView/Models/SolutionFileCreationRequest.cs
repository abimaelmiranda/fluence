using Fluence.Modules.SolutionView.Models.Enums;

namespace Fluence.Modules.SolutionView.Models;

public sealed record SolutionFileCreationRequest(string Name, SolutionFileKind Kind);