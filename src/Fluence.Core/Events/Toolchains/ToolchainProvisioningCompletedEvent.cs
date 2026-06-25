using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Toolchains;

namespace Fluence.Core.Events.Toolchains;

public sealed record ToolchainProvisioningCompletedEvent(
    string ToolchainId,
    ToolchainCapability Capability) : IShellEvent;
