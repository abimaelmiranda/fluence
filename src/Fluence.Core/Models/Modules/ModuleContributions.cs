using Fluence.Core.Models.Output;

namespace Fluence.Core.Models.Modules;

public sealed class ModuleContributions
{
    public static ModuleContributions Empty { get; } = new();

    public IReadOnlyList<ShellPanelContribution> Panels { get; init; } = [];

    public OutputChannelDescriptor? OutputChannel { get; init; }
}
