using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Models.Debugging.Enums;

namespace Fluence.Core.Models.Debugging;

public sealed record DebugAdapterSession(
    IDebugAdapterClient Adapter,
    DebugLaunchRequest LaunchRequest,
    DebugExceptionBreakMode ExceptionBreakMode);
