using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Git;

public sealed record GitCheckoutCompletedEvent(string BranchName) : IShellEvent;
