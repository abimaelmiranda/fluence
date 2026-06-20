using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Workspace;

public sealed record OpenSpecializedFileRequestedEvent(string Path) : IShellEvent;
