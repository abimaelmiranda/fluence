using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Core.Requests.Lsp;

public sealed record GoToDefinitionRequest(string FilePath, int Line, int Character) : IShellRequest<LspLocation>;
