using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Build;

public sealed record TestProjectRequestedEvent(string ProjectPath) : IShellEvent;
