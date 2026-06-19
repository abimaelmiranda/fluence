using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Lsp;

public sealed record SemanticTokensRefreshStartedEvent(string FilePath) : IShellEvent;
