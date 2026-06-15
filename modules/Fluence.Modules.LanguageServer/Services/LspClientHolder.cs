using Fluence.Infrastructure.Protocols.Lsp;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed class LspClientHolder
{
    public LspClient? Client { get; set; }
}
