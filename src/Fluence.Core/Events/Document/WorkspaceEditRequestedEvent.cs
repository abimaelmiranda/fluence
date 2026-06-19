using System.Collections.Generic;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Core.Events.Document;

public sealed record WorkspaceEditRequestedEvent(string FilePath, IReadOnlyList<LspTextEdit> Edits) : IShellEvent;
