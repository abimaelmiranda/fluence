using Fluence.Core.Models.Modules.Enums;

namespace Fluence.Core.Models.Modules;

public sealed record ShellRegionContent(
    ShellRegion Region,
    string ContentId,
    string Title,
    object ViewModel);
