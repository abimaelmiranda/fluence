using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Lsp;

public sealed record SemanticTokensRefreshFinishedEvent(string FilePath) : IShellEvent;
