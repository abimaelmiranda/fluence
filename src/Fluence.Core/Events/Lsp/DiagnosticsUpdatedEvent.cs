using System.Collections.Generic;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Core.Events.Lsp;

public sealed record DiagnosticsUpdatedEvent(string FilePath, IReadOnlyList<LspDiagnostic> Diagnostics) : IShellEvent;
