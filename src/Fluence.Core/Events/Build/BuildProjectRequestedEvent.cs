using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Build;

public sealed record BuildProjectRequestedEvent(string ProjectPath) : IShellEvent;
