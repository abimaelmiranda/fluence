using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Document;

public sealed record FlushDocumentSyncEvent(string FilePath) : IShellEvent;
