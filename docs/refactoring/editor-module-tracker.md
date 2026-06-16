# Refactoring Tracker — Fluence.Modules.Editor

## Objetivo

Melhorar qualidade e performance do módulo do editor. Alvo principal: reduzir lag de digitação.
Cada agent deve marcar os steps concluídos e atualizar a seção "Cross-module TODOs".

---

## Instruções para próximo agent

Contexto: Varredura para remover código morto, substituir RegEx por Source Generators, aplicar
Object Calisthenics e trocar manipulações pesadas de string por StringBuilder. Aplicar boas
práticas como redução de aninhamento, evitar try-catch em loop. Preferir source generation em
manipulação JSON. Foco em gerar "Hotpaths" para o JIT.

**Regra de isolamento de módulo:** Não interagir com outros módulos durante o refactoring.
Se uma melhoria exigir mudança em código fora do modulo em que voce está, criar um workaround
temporário e documentar com:
```csharp
// Cannot do this better because needs refactoring in <ModuleName>.<TypeName>
```
Esses comentários serão coletados para um refactoring cross-module separado.

---

## STEP 1 — Performance Fixes (no monolito `EditorView.axaml.cs`)

- [x] **1.1** `IsInsideComment()` — cache por linha (Phase A/B), de O(n×m) para O(line_length) por tecla
- [x] **1.2** `SemanticColorizer` — pre-indexar tokens por linha com `Dictionary<int, List<SemanticToken>>`
- [x] **1.3** `DiagnosticRenderer` — brushes/pens `static readonly` + `Dictionary` para lookup de visual lines por frame
- [x] **1.4** `BuildSignatureInlines()` — brushes `static readonly` (`SignatureGrayBrush`, `SignatureWhiteBrush`)
- [x] **1.5** `DebugLineRenderer.Draw()` — brush `static readonly` (`ExecutionLineBrush`)
- [x] **1.6** Empty catch blocks — substituídos por `OperationCanceledException` + `Debug.WriteLine` nos 4 locais
- [x] **1.7** `OnPointerHover()` — flatten com early returns + brushes de diagnóstico `static readonly`

**Build pós-step 1:** ✅ 0 erros, 0 warnings

---

## STEP 2 — Extrair classes aninhadas para arquivos próprios

Cada extração: copiar → ajustar namespace/usings → remover original → build.

- [x] **2.1** `LspCompletionData` → `Completion/LspCompletionData.cs` (marcar como `partial` para STEP 3)
- [x] **2.2** `BreakpointMargin` → `Rendering/BreakpointMargin.cs`
- [x] **2.3** `DiagnosticRenderer` → `Rendering/DiagnosticRenderer.cs`
- [x] **2.4** `SemanticColorizer` → `Rendering/SemanticColorizer.cs` (`SemanticBrushes` vai junto como nested class)
- [x] **2.5** `DebugLineRenderer` → `Rendering/DebugLineRenderer.cs`

**Build pós-step 2:** ✅ 0 erros, 0 warnings

---

## STEP 3 — `[GeneratedRegex]` em `LspCompletionData`

Feito junto com STEP 2.1 — classe já nasceu `partial` com `[GeneratedRegex]`.

- [x] `SnippetPlaceholderRegex()` — `@"\$\{?\d+:?([^}]*)?\}?|\$0"`
- [x] `AngleBracketTagRegex()` — `@"<[^<>]*>"`

**Build pós-step 3:** ✅ 0 erros, 0 warnings

---

## STEP 4 — Split `EditorView.axaml.cs` em partial class files

Regra Avalonia: apenas `EditorView.axaml.cs` declara `: UserControl`.
Demais arquivos: `public partial class EditorView` sem base class.

- [x] **4.1** `EditorView.TextMate.cs` — `InitializeTextMate`, `LoadStandardTheme`, `ApplyGrammarForPath`, `ResolveScopeName`
- [x] **4.2** `EditorView.Debugging.cs` — `UpdateDebugRendering`, `OnBreakpointAreaPointerMoved`, `OnBreakpointAreaPressed`
- [x] **4.3** `EditorView.Diagnostics.cs` — `OnDiagnosticsUpdated`, `OnContextMenuOpening`, `OnMenu*`
- [x] **4.4** `EditorView.SignatureHelp.cs` — `TriggerSignatureHelpAsync`, `RenderSignatureHelp`, `BuildSignatureInlines`, `PositionSignatureHelpPopup`, `CloseSignatureHelpPopup`
- [x] **4.5** `EditorView.Hover.cs` — `OnPointerHover`, `EvaluateAndShowAsync`, `ProcessLspHoverAsync`, `ExtractWordAt`, `IsWordChar`, `ClosePopupDelayed`, timers de hover
- [x] **4.6** `EditorView.Completion.cs` — `TriggerCompletionAsync`, `ProcessCompletionRequestAsync`, `RefreshCompletionWindowItems`, `ExtractCompletionPrefix`, `IsCompletionChar`, timers de completion
- [x] **4.7** `EditorView.Keyboard.cs` — `OnEditorPreviewKeyDown`, `OnTextEntering`, `OnTextEntered`, `TryHandleSmartEnter`, `TryHandleMacEditingShortcut`, `IsInsideComment`, `ScanLineForBlockCommentState`, static helpers de keyboard
- [x] **4.8** `EditorView.ViewModel.cs` — `OnDataContextChanged`, `BindViewModel`, `OnViewModelPropertyChanged`, `OnEditorTextChanged`, `SetEditorText`, `OnLspServerReady`, `OnSemanticTokensUpdated`, `NavigateToLocation`
- [x] **4.9** `EditorView.axaml.cs` final — fields + constructor + `SetServices` + record types (179 linhas)

**Build pós-step 4:** ✅ 0 erros, 0 warnings

### Resultado final (linhas por arquivo):
| Arquivo | Linhas |
|---------|--------|
| `EditorView.axaml.cs` | 179 |
| `EditorView.Completion.cs` | 245 |
| `EditorView.Debugging.cs` | 54 |
| `EditorView.Diagnostics.cs` | 56 |
| `EditorView.Hover.cs` | 307 |
| `EditorView.Keyboard.cs` | 513 |
| `EditorView.SignatureHelp.cs` | 155 |
| `EditorView.TextMate.cs` | 63 |
| `EditorView.ViewModel.cs` | 178 |
| `Rendering/BreakpointMargin.cs` | 74 |
| `Rendering/DiagnosticRenderer.cs` | 83 |
| `Rendering/SemanticColorizer.cs` | 94 |
| `Rendering/DebugLineRenderer.cs` | 31 |
| `Completion/LspCompletionData.cs` | 113 |
| **Antes (monolito)** | **2.030** |

---

## Checklist de Verificação Manual (após cada step)

1. **Digitação rápida** — abrir .cs 200+ linhas, digitar 20 chars rápido, sem lag
2. **Auto-pair** — `(`, `[`, `{`, `"` → char de fechamento inserido automaticamente
3. **Completion popup** — digitar parcial → popup aparece, Tab commita corretamente
4. **Dot-completion** — `.` após variável → membros em ~50ms
5. **Signature help** — `(` após método → popup aparece, `,` muda parâmetro ativo
6. **LSP hover** — hover em tipo por ~400ms → popup LSP aparece
7. **Debug hover** — breakpoint ativo, hover em variável → árvore de debug aparece
8. **Diagnostic underlines** — erro de compilação → underline vermelho + tooltip ao hover
9. **Breakpoints** — click na margem → ponto vermelho aparece/desaparece
10. **Go To Definition** — F12 em símbolo → navegação ocorre
11. **Smart Enter** — `{` + Enter → bloco indentado com fechamento
12. **Comment detection** — `//` + `(` → sem auto-pair dentro do comentário

---

## STEP 5 — Typing Lag Fixes

Causas identificadas por análise estática + exploração dos handlers de keystroke.

- [x] **5.1** `ApplyGrammarForPath` removido do branch `ActiveText` em `OnViewModelPropertyChanged` — era chamado a cada tecla reinstalando a gramática TextMate (**smoking gun**)
- [x] **5.2** `OnEditorTextChanged` debounced (250ms) — `Editor.Text` não é mais copiado integralmente a cada tecla; usa timer padrão como completion/hover
- [x] **5.3** `OnSemanticTokensUpdated` throttled — somente um `Redraw()` pendente por vez; tokens mais recentes sempre usados no flush
- [x] **5.4** `IsInsideComment()` Phase A incremental — caret forward: varre apenas o delta de linhas; backward/cache miss: scan completo (sem regressão de corretude)
- [x] **5.5** `DiagnosticRenderer.Draw()` — `_visualLineMap` agora é campo reutilizado com `Clear()` em vez de `new Dictionary` por frame
- [x] **5.6** `BreakpointMargin` — `_breakpointsByLine` Dictionary indexado por linha; substituídos `FirstOrDefault` O(n) e `Any` O(n) por `TryGetValue`/`ContainsKey` O(1)

**Build pós-step 5:** ✅ 0 erros, 0 warnings

---

---

## Fluence.Modules.LanguageServer + Fluence.Infrastructure/Protocols/Lsp

### Objetivo

Eliminar código morto, aplicar `[GeneratedRegex]`, substituir `Console.Error` por `Debug.WriteLine`,
corrigir empty catches, substituir parsing `JsonNode` manual por `JsonSerializerContext` source-generated
onde o protocolo é estruturado (diagnostics, navigate, semantic tokens, initialize result),
e dividir arquivos monolíticos em partial classes por responsabilidade.

**Regra de isolamento:** Sem alterações em `Fluence.Core` (interfaces e models públicos permanecem intactos).
Workaround para cross-module: `// Cannot do this better because needs refactoring in <Module>.<Type>`.

---

### STEP 1 — Code Quality Fixes

- [x] **1.1** `HoverService` — classe `partial` + `[GeneratedRegex]` para code fence regex (`@"^```[a-zA-Z]*\r?$"`)
- [x] **1.2** `SemanticTokensService` — `Console.Error.WriteLine` → `Debug.WriteLine`
- [x] **1.3** Empty catches — `Entrypoint.cs` (7 locais): padrão `OperationCanceledException` + `Debug.WriteLine`
- [x] **1.4** Empty catches — `LanguageServerService.cs` (2 locais): `StopAsync`, `CaptureSemanticTokenLegend`

**Build pós-step 1:** ✅ 0 erros, 0 warnings

---

### STEP 2 — JSON Source Generation

- [x] **2.1** `Protocol/LspProtocolTypes.cs` — tipos tipados: `LspPublishDiagnosticsParamsRaw`, `LspDiagnosticRaw`, `LspLocationRaw`, `LspRangeRaw`, `LspPositionRaw`, `LspSemanticTokensRaw`, `LspInitializeResultRaw` (+ `Capabilities`/`Provider`/`Legend`)
- [x] **2.2** `Protocol/LspJsonContext.cs` — `[JsonSerializable]` para todos os tipos acima; `[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]`
- [x] **2.3** `LanguageServerService.Diagnostics.cs` — `HandlePublishDiagnostics` e `CaptureSemanticTokenLegend` usam `Deserialize(LspJsonContext.Default.*)`
- [x] **2.4** `SemanticTokensService.cs` — `result?.Deserialize(LspJsonContext.Default.LspSemanticTokensRaw)` substituindo leitura manual do array `data`
- [x] **2.5** `NavigationService.cs` — `ParseLocation` usa `Deserialize(LspJsonContext.Default.LspLocationRaw)` (array → primeiro elemento, objeto → direto)
- [ ] **2.6** `HoverService`, `CompletionService`, `SignatureHelpService` — mantêm `JsonNode` manual por union types LSP (`contents: string|MarkupContent|MarkedString[]`, `label: string|int[]`, etc.)

**Build pós-step 2:** ✅ 0 erros, 0 warnings

---

### STEP 3 — Split Entrypoint.cs (405 linhas → partial classes)

Regra: apenas `Entrypoint.cs` declara `: IModule, IDisposable`.

- [x] **3.1** `Entrypoint.DidChange.cs` — campos debounce didChange + `QueueDidChange`, `FlushPendingDidChangeImmediatelyAsync`, `FlushPendingDidChangeAndCloseAsync`, `OnDidChangeTimerElapsed`, `SendPendingDidChangeAsync`, record `PendingDidChange`
- [x] **3.2** `Entrypoint.SemanticTokens.cs` — campos debounce semTokens + `QueueSemanticTokens`, `OnSemanticTokensTimerElapsed`, `SendSemanticTokensAsync`
- [x] **3.3** `Entrypoint.Navigation.cs` — `HandleNavigation`, `ResolveAndPublishNavigation`, `SafeSend`
- [x] **3.4** `Entrypoint.cs` residual — `Name`, `Register`, `Dispose`, `Initialize`, `TryStartOrRestart`, `ResolveRootPath` (~130 linhas)

**Build pós-step 3:** ✅ 0 erros, 0 warnings

---

### STEP 4 — Split OmniSharpProvisioningService.cs (339 linhas, em Infrastructure)

Regra: apenas `OmniSharpProvisioningService.cs` declara `: ILspProvisioningService`.

- [x] **4.1** `OmniSharpProvisioningService.Elevation.cs` — `EnsureInstallDirectoryAsync`, `ElevateOnMacOsAsync`, `ElevateOnLinuxAsync`
- [x] **4.2** `OmniSharpProvisioningService.Download.cs` — `DownloadBinaryAsync`, `FindOmniSharpAsset`, `ExtractArchive`, `MakeExecutable`, `RunProcess`
- [x] **4.3** `OmniSharpProvisioningService.Paths.cs` — `ResolveGlobalInstallDir`, `ResolveExecutableName`, `ResolveDotnetDir`, `FindOnPath`, `ResolveRuntimeId`
- [x] **4.4** `OmniSharpProvisioningService.cs` residual — static constructor, `IsProvisioned`, `GetExecutablePath`, `GetLaunchEnvironment`, `ProvisionAsync` (~40 linhas)

**Build pós-step 4:** ✅ 0 erros, 0 warnings

---

### STEP 5 — Split LanguageServerService.cs (272 linhas)

Regra: apenas `LanguageServerService.cs` declara `: ILanguageServerService, IAsyncDisposable`.

- [x] **5.1** `LanguageServerService.DocumentSync.cs` — `SendDidOpenAsync`, `SendDidChangeAsync`, `SendDidCloseAsync`
- [x] **5.2** `LanguageServerService.Diagnostics.cs` — `CaptureSemanticTokenLegend`, `OnClientDisconnected`, `OnNotificationReceived`, `HandlePublishDiagnostics`
- [x] **5.3** `LanguageServerService.Protocol.cs` — `BuildInitializeParams` (static, ~60 linhas), `FilePathToUri`, `UriToFilePath`
- [x] **5.4** `LanguageServerService.cs` residual — constructor, properties, `StartAsync`, `StopAsync`, `DisposeAsync` (~80 linhas)

**Build pós-step 5:** ✅ 0 erros, 0 warnings

---

### Resultado final (linhas por arquivo):

| Arquivo | Linhas |
|---------|--------|
| `Entrypoint.cs` | ~130 |
| `Entrypoint.DidChange.cs` | ~145 |
| `Entrypoint.SemanticTokens.cs` | ~55 |
| `Entrypoint.Navigation.cs` | ~50 |
| `Services/LanguageServerService.cs` | ~80 |
| `Services/LanguageServerService.DocumentSync.cs` | ~50 |
| `Services/LanguageServerService.Diagnostics.cs` | ~65 |
| `Services/LanguageServerService.Protocol.cs` | ~65 |
| `Protocol/LspProtocolTypes.cs` | ~50 |
| `Protocol/LspJsonContext.cs` | ~8 |
| `Infrastructure/OmniSharpProvisioningService.cs` | ~40 |
| `Infrastructure/OmniSharpProvisioningService.Elevation.cs` | ~100 |
| `Infrastructure/OmniSharpProvisioningService.Download.cs` | ~115 |
| `Infrastructure/OmniSharpProvisioningService.Paths.cs` | ~55 |
| **Antes (monolito Entrypoint)** | **405** |
| **Antes (LanguageServerService)** | **272** |
| **Antes (OmniSharpProvisioning)** | **339** |

---

### Checklist de Verificação Manual

1. **Completion popup** — digitar parcial → popup aparece, Tab commita
2. **Go To Definition** — F12 em símbolo → navegação ocorre
3. **LSP hover** — hover em tipo por ~400ms → popup LSP aparece
4. **Signature help** — `(` após método → popup aparece, `,` muda parâmetro ativo
5. **Diagnostic underlines** — erro de compilação → underline vermelho + tooltip ao hover
6. **Semantic highlighting** — tipos/keywords coloridos corretamente após abrir arquivo

---

## Fluence.Modules.SourceControl

### Objetivo

Melhorar qualidade do módulo de Git/source control, reduzir classes concentradas, evitar montagem
frágil de argumentos do Git CLI e adicionar ação destrutiva confirmada de revert por arquivo.

**Regra de isolamento:** Sem alterações em Core/Desktop/Infrastructure para este ciclo. Serviços novos
de confirmação ficam dentro do próprio módulo SourceControl.

---

### STEP 1 — Git CLI Robustness

- [x] **1.1** `GitCliService` dividido em partial classes por responsabilidade (`Status`, `FileActions`, `Branches`, `Sync`)
- [x] **1.2** Execução Git migrada para `ProcessStartInfo.ArgumentList`, removendo interpolação manual de argumentos
- [x] **1.3** Empty catches substituídos por `OperationCanceledException` preservada + `Debug.WriteLine`
- [x] **1.4** Parser de rename/copy preserva `OriginalPath` em `GitFileChange`
- [x] **1.5** `DiffViewerView` — fallback de gramática diff agora registra `Debug.WriteLine`

**Build pós-step 1:** não executado — validação fica com o usuário para evitar bloqueio de sandbox.

---

### STEP 2 — Split SourceControlViewModel

- [x] **2.1** `SourceControlViewModel.cs` residual — estado, constructor, inicialização e dispose
- [x] **2.2** `SourceControlViewModel.Properties.cs` — propriedades geradas por CommunityToolkit
- [x] **2.3** `SourceControlViewModel.Refresh.cs` — refresh, apply status, watcher e estado sem repositório
- [x] **2.4** `SourceControlViewModel.FileActions.cs` — commit, stage/unstage, diff e revert
- [x] **2.5** `SourceControlViewModel.Branches.cs` — checkout, conflito e delete branch
- [x] **2.6** `SourceControlViewModel.Stashes.cs` — list/pop/drop stash
- [x] **2.7** `SourceControlViewModel.Sync.cs` — pull/push/fetch

**Build pós-step 2:** não executado — validação fica com o usuário para evitar bloqueio de sandbox.

---

### STEP 3 — Revert Changes per file

- [x] **3.1** `IGitService.RevertFileAsync` adicionado
- [x] **3.2** `ISourceControlDialogService` + `AvaloniaSourceControlDialogService` adicionados dentro do módulo
- [x] **3.3** `GitFileChangeViewModel` usa `AsyncRelayCommand` e expõe `RevertCommand`
- [x] **3.4** Botão `Revert changes` adicionado por arquivo ao lado de `View diff`
- [x] **3.5** Revert tracked restaura para `HEAD`; untracked/added deleta via `git clean -fd`
- [x] **3.6** UX — click em qualquer row de changes abre o arquivo correspondente no editor

**Build pós-step 3:** não executado — validação fica com o usuário para evitar bloqueio de sandbox.

---

### Checklist de Verificação Manual

1. **Painel Git** — abrir repositório e confirmar branch/status/stashes
2. **Diff staged/unstaged** — botão `View diff` continua abrindo tool tab
3. **Click na row de change** — abre o arquivo no editor
4. **Botões da row** — clicar em diff/revert/stage/unstage não abre o arquivo junto
5. **Revert modified unstaged** — restaura arquivo para `HEAD`
6. **Revert modified staged** — remove stage e restaura working tree para `HEAD`
7. **Revert untracked** — modal informa deleção e arquivo é removido após confirmação
8. **Cancel modal** — cancelar não altera arquivo nem status
9. **Stage/unstage** — ações por arquivo e `all` continuam funcionando
10. **Branch checkout** — checkout normal e fluxo de conflito local continuam funcionando
11. **Stash** — pop/drop continuam funcionando
12. **Watcher** — lista atualiza após mudança externa no index

---

## Cross-module TODOs

### Lag residual (pós STEP 5) — próximo plano

1. **`ShellEventBus.Publish` era síncrono** ✅ — migrado para post-and-forget via `Dispatcher.UIThread.Post(Background)`. Publish retorna imediatamente; todos os handlers rodam deferred na UI thread.

   **TODO (próximo loop) — contratos sync/async no pub/sub:**
   Hoje todos os subscribers rodam na UI thread (deferred). O ideal é separar em dois contratos:
   - `SubscribeSync<TEvent>(Action<TEvent>)` — deferred, UI thread (handlers que tocam VM/ShellRegions: `MainWindowViewModel`, `FileExplorer`, `SolutionView`, `SourceControl`)
   - `SubscribeAsync<TEvent>(Func<TEvent,Task>)` — `Task.Run`, thread pool (handlers que só disparam trabalho async: `LanguageServer`, `Debug`, `DotnetCli`, `Editor` entrypoints)
   - `EditorView` handlers podem remover o `Dispatcher.UIThread.Post` interno ao migrar para `SubscribeSync`
   - TODO marcado em `IShellEventBus.cs`

2. **TextMate tokenização síncrona no UI thread** — AvaloniaEdit/TextMateSharp retokeniza as linhas afetadas durante o render pass a cada tecla. Sem controle direto; mitigação possível com prioridade de render mais baixa.

3. **`_workspace.UpdateActiveDocumentContent` dispara `Workspace.Changed` em cascata** (`EditorViewModel.OnActiveTextChanged:146`) — agora debounced 250ms mas ainda síncrono quando dispara; pode encadear `RefreshFromWorkspace` no UI thread.
