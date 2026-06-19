using Fluence.Core.Models.Modules.Enums;

namespace Fluence.Core.Models.Modules;

public sealed record ShellPanelContribution(
    ShellRegion Region,
    string ContentId,
    string Title,
    Func<IServiceProvider, object> ResolveViewModel,
    PanelVisibilityRule Visibility,
    int Order = 0);
