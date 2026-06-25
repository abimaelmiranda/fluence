using Fluence.Core.Models.Toolchains;

namespace Fluence.Core.Abstractions.Toolchains;

public interface IToolchainRegistry
{
    void Register(IToolchain toolchain);

    IToolchain? GetById(string id);

    IToolchain Active { get; }

    Task ExecuteAsync(ToolchainCommand command, CancellationToken cancellationToken = default);
}
