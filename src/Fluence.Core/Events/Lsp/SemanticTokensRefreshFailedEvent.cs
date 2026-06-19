using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Lsp;

public sealed record SemanticTokensRefreshFailedEvent(string FilePath) : IShellEvent;
