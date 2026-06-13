# Terminal Module — Sensitive Area

> **⚠️ Este módulo é sensível a mudanças.** Qualquer alteração nos arquivos abaixo pode quebrar silenciosamente o terminal interativo, o startup do shell, ou TUI apps.

---

## Arquivos críticos

| Arquivo | Responsabilidade | Risco |
|---|---|---|
| `Terminal/TerminalControl.cs` | Renderização Avalonia do buffer XTerm.NET | Mudanças no loop de render ou na leitura do buffer quebram toda a exibição |
| `ViewModels/TerminalSessionViewModel.cs` | Bridge PTY ↔ XTerm.NET por sessão | Timing do `DataReceived` bridge é frágil; wiring errado corrompe stdin do zsh |
| `Views/TerminalView.axaml.cs` | Startup do shell e resize inicial | Ordem de inicialização importa: resize deve preceder o start do shell |

A infraestrutura PTY está em `src/Fluence.Infrastructure/Pty/` e `src/Fluence.Infrastructure/TerminalService.cs`.

---

## Invariantes que não devem ser quebradas

### PTY I/O
- `MacOsPtySession` usa **dois `FileStream` separados** (reader + writer) sobre o mesmo fd com `ownsHandle: false`. Nunca consolidar em um único stream — acesso concorrente de read loop e write path corrompe o FileStream.
- O fd real é fechado explicitamente em `Dispose()` porque ambos os streams têm `ownsHandle: false`.
- Writes no PTY devem ter `Flush()` imediato — o `FileStream` com `bufferSize: 1024` acumula em buffer interno e teclas digitadas nunca chegam ao shell sem flush.

### Bridge XTerminal → PTY (`DataReceived`)
- O XTerm.NET dispara `Terminal.DataReceived` **durante** a chamada a `_xterm.Write()` para responder a queries de terminal (DA1/DA2, cursor position).
- Essas respostas **devem chegar ao PTY** para TUI apps funcionarem corretamente.
- Elas **não devem chegar antes do zle do zsh estar pronto** — do contrário corrompem o stdin e zsh lança "can't open input file".
- A solução atual usa um delay de 500 ms pós-`StartShellAsync` antes de assinar o evento.

### Startup do shell
- O shell só deve iniciar **após** o painel estar visível e ter bounds reais.
- Iniciar com `cols=0` ou `rows=0` faz o prompt do zsh chegar antes do primeiro resize e ser descartado.
- O `RequestInitialResize()` garante que o callback `Resized` dispare mesmo se `ArrangeOverride` já rodou antes do `DataContext` chegar.

### Renderização (TerminalControl)
- Usar `buffer.YDisp` (scroll offset) ao indexar `buffer.Lines[]` — nunca indexar a partir de 0.
- `buffer.Length` pode ser 0 em terminais recém-criados; iterar por `terminal.Rows` em vez de `buffer.Length`.
- Células com `Width == 0` são continuação de wide-char e devem ser puladas.
- Atributos de célula renderizados: **inverse video** (troca fg/bg), **bold**, **italic**, **underline**.
  - Sem inverse video, barras de status de TUI ficam invisíveis.
  - Sem bold, labels e cabeçalhos ficam flat.
- O redraw é disparado por `BufferRefreshed` (após cada `_xterm.Write()`) **além** dos eventos `LineFed`/`BufferChanged`/`Scrolled` — necessário para caracteres comuns e sequências de posicionamento de cursor que não geram line feed.

---

## O que já foi depurado (não reverter sem motivo)

- **`FileStream` sem `Flush()`**: input do usuário ficava em buffer e nunca chegava ao shell.
- **Bridge `DataReceived` síncrono**: respondia queries de terminal *durante* `_xterm.Write()`, antes do zle estar pronto, corrompendo o stdin.
- **Shell iniciando com bounds zero**: prompt chegava antes do resize e era descartado.
- **Apenas `LineFed` como trigger de redraw**: caracteres digitados e prompt inicial não apareciam até o próximo Enter.
- **`_xterm.DataReceived` desconectado**: TUI apps não recebiam respostas a queries de terminal e renderizavam modo degradado.
- **Stream único para read+write**: acesso concorrente corrompia o `FileStream` e travava o terminal.
