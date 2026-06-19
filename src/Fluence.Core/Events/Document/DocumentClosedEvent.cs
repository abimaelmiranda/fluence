using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Document;

public sealed record DocumentClosedEvent(string FilePath) : IShellEvent;
