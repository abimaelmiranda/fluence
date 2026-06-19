using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Workspace;

public sealed record OpenFileAtLocationRequestedEvent(string Path, int Line, int Character) : IShellEvent;
