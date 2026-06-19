namespace Fluence.Core.Models.Lifecycle;

public sealed record ApplicationShutdownRequest(
    ApplicationShutdownReason Reason,
    int ExitCode = 0,
    bool RequestNativeShutdown = true);
