using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Document;

public sealed record DocumentLiveChangedEvent(string FilePath, string Content, int Version, bool FlushImmediately) : IShellEvent;
