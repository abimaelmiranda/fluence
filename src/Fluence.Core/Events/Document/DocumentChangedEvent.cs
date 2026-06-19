using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Document;

public sealed record DocumentChangedEvent(string FilePath, string Content, int Version) : IShellEvent;
