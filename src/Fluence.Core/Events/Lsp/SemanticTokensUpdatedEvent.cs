using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Core.Events.Lsp;

public sealed record SemanticTokensUpdatedEvent(string FilePath, int Version, SemanticToken[] Tokens) : IShellEvent;
