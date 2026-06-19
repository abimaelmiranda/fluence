using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Workspace;

public sealed record PublishProjectRequestedEvent(string? ProjectPath = null) : IShellEvent;
