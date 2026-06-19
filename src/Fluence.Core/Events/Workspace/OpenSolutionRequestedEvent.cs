using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Workspace;

public sealed record OpenSolutionRequestedEvent(string Path) : IShellEvent;
