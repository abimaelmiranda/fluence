namespace Fluence.Core.Models.Toolchains;

public sealed record ToolchainCapabilities(ToolchainCapability Value)
{
    public bool Has(ToolchainCapability capability) => (Value & capability) == capability;
}
