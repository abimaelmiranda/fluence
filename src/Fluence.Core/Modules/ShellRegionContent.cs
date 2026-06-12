namespace Fluence.Core.Modules;

public sealed record ShellRegionContent(
    ShellRegion Region,
    string ContentId,
    string Title,
    object ViewModel);
