using Fluence.Core.Abstractions.Debugging;

namespace Fluence.Core.Abstractions.Toolchains;

public interface IDebugToolchain : IToolchain
{
    IDebugService Debug { get; }
}
