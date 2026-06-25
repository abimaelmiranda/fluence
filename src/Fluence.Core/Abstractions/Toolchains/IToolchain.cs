using Fluence.Core.Models.Toolchains;

namespace Fluence.Core.Abstractions.Toolchains;

public interface IToolchain
{
    string Id { get; }

    string LanguageId { get; }

    ToolchainSupportLevel SupportLevel { get; }

    ToolchainCapabilities Capabilities { get; }

    Task<bool> EnsureAsync(ToolchainCapability capability, CancellationToken cancellationToken = default);

    Task ExecuteAsync(ToolchainCommand command, CancellationToken cancellationToken = default);
}
