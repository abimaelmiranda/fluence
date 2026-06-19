using System.Threading;
using Fluence.Core.Models.Lifecycle;

namespace Fluence.Core.Models.Modules;

public sealed record ModuleShutdownContext(
    ApplicationShutdownReason Reason,
    CancellationToken CancellationToken);
