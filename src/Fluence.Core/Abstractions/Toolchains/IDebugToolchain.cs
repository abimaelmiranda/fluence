using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.Debugging;

namespace Fluence.Core.Abstractions.Toolchains;

public interface IDebugToolchain : IToolchain
{
    Task<DebugAdapterSession?> PrepareDebugSessionAsync(CancellationToken ct = default);
}
