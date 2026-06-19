using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Document;

public sealed record DocumentOpenedEvent(string FilePath, string Content, string LanguageId, int Version) : IShellEvent;
