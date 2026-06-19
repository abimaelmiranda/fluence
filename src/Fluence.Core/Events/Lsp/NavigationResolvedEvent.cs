using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Lsp;

public sealed record NavigationResolvedEvent(string FilePath, int Line, int Character) : IShellEvent;
