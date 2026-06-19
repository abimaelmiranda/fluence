using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Build;

public sealed record RunSpecificProjectRequestedEvent(string ProjectPath) : IShellEvent;
