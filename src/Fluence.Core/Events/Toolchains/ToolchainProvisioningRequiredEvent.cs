using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Toolchains;

namespace Fluence.Core.Events.Toolchains;

public sealed record ToolchainProvisioningRequiredEvent(
    string ToolchainId,
    ToolchainCapability Capability) : IShellEvent;
