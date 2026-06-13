# Fluence IDE — Especificação MVP do Terminal Integrado (PTY + Emulador VT)

## Contexto
A Fluence IDE é uma IDE nativa cross-platform (macOS/Windows) em Avalonia UI + C#/.NET 8. O terminal integrado apresenta:

- **Intermitência:** às vezes o shell não inicializa.
- **Flicker / output some:** ao digitar comandos, o conteúdo pisca e desaparece.
- Implementação atual tentou usar `fork()` via P/Invoke no .NET, o que é **inválido** em runtime multi-threaded.

A referência conceitual é a arquitetura do VS Code: **PTY backend** + **emulador VT**.

## Diagnóstico (causa raiz)

### 1) `fork()` em .NET é proibido
O .NET runtime é multi-threaded (GC, finalizer, thread pool). Ao chamar `fork()`, apenas a thread atual é copiada no filho; o estado interno do runtime pode ficar inconsistente (locks travados, heap/GC em estado indefinido). Qualquer execução de código managed entre `fork()` e `exec()` é **undefined behavior**.

**Consequência:** comportamento intermitente ("hora funciona, hora não").

### 2) Falta de emulador VT (ANSI/VT100)
Shells e programas interativos emitem sequências ANSI/VT (ex.: limpar tela, mover cursor, apagar linha, mudar cores). Se a UI recebe bytes brutos e apenas "append" texto, as sequências provocam:

- limpeza inesperada (parece que o output some)
- reposicionamento de cursor sem interpretação correta (parece flicker)

**Consequência:** output instável mesmo quando o PTY está correto.

## Objetivo
Implementar um terminal integrado estável, com quatro camadas bem definidas:

1. **PTY Spawn (OS integration)**: criar PTY/ConPTY e iniciar o shell de forma segura.
2. **I/O Loop (stream bridge)**: loop eficiente de leitura/escrita de bytes com batching.
3. **Emulador VT (parser + screen buffer)**: interpretar ANSI/VT e manter estado da tela.
4. **UI (Avalonia renderer)**: renderizar o buffer de células e encaminhar input do teclado.

## Arquitetura (camadas)

```
┌─────────────────────────────────────────────────────────────┐
│  4. UI Layer (Avalonia)                                     │
│     - Renderiza ScreenBuffer (células)                      │
│     - Input teclado → bytes                                 │
│     - Atualiza a 60fps (batching)                           │
└──────────────────────┬──────────────────────────────────────┘
                       │ snapshot do buffer + cursor
┌──────────────────────▼──────────────────────────────────────┐
│  3. Emulador VT (ANSI/VT parser + ScreenBuffer)             │
│     - Interpreta CSI/SGR/clear/move cursor                  │
│     - Mantém matriz de Cell e scrollback                    │
└──────────────────────┬──────────────────────────────────────┘
                       │ bytes (ReadOnlySpan<byte>)
┌──────────────────────▼──────────────────────────────────────┐
│  2. I/O Loop (Stream bridge)                                │
│     - ReadAsync/WriteAsync no master FD                     │
│     - ArrayPool<byte> + Channel para batching               │
│     - NUNCA converter pra string aqui                       │
└──────────────────────┬──────────────────────────────────────┘
                       │ FD do kernel
┌──────────────────────▼──────────────────────────────────────┐
│  1. PTY Spawn (macOS/Windows)                               │
│     - macOS: openpty + posix_spawn (NUNCA fork)             │
│     - Windows: ConPTY                                       │
│     - Resize via ioctl(TIOCSWINSZ) / ResizePseudoConsole     │
└─────────────────────────────────────────────────────────────┘
```

## Especificação técnica — Camada 1: PTY (macOS)

### Regras
- **Remover totalmente `fork()`**.
- Usar **`posix_spawn` + `posix_spawn_file_actions` + `posix_spawnattr`**.
- `openpty()` cria `masterFd` e `slaveFd`.
- `posix_spawn_file_actions_adddup2(slave, 0/1/2)` para conectar stdin/stdout/stderr ao PTY.
- `posix_spawn_file_actions_addclose(master)` e `addclose(slave)` para evitar herança.
- `posix_spawnattr_setsid_np` (macOS) para iniciar o processo em nova sessão (equivalente a `setsid`).
- `TERM=xterm-256color` no environment (se ausente).

### Resize
- `ioctl(masterFd, TIOCSWINSZ, ref WinSize)`.

### Encerramento
- Matar **process group** do filho (ex.: `kill(-pid, SIGTERM)`) para garantir que subprocessos também morram.
- `waitpid(pid, ...)` para evitar zombies.

### O que é proibido
- `getdtablesize()` + loop `close(fd)`.
- `execve` com arrays marshaled de forma incerta.
- `ProcessStartInfo.RedirectStandardOutput` como substituto de PTY.

## Especificação técnica — Camada 2: I/O Loop

### Regras
- Ler do `masterFd` com `ReadAsync` e buffer reutilizável (`ArrayPool<byte>`).
- Enviar bytes lidos para o emulador VT **sem converter para string**.
- Usar `Channel` (ou buffer circular) para batching e evitar atualizar UI a cada chunk.
- UI consome a 60fps (ex.: `DispatcherTimer`) e solicita snapshot.

### Antipadrões
- `Encoding.UTF8.GetString()` em todo chunk (gera muitas alocações).
- Atualizar `TextBox.Text`/`TextBlock.Text` a cada read.

## Especificação técnica — Camada 3: Emulador VT (MVP)

### ScreenBuffer
- Buffer de tela como array prealocado (ex.: `Cell[]`).
- `Cell` deve ser `struct`.

Exemplo (MVP):

- `char`/`Rune`
- `Foreground`, `Background`
- `Flags` (bold/underline/inverse)

### Parser ANSI/VT (mínimo)
Suportar (MVP):

- CSI `H`/`f`: mover cursor (row;col)
- CSI `A`/`B`/`C`/`D`: mover cursor relativo
- CSI `G`: mover para coluna N
- CSI `J`: limpar display (`0/1/2/3`)
- CSI `K`: limpar linha (`0/1/2`)
- CSI `m`: SGR (pelo menos `0`, `1`, `30-37`, `40-47`; opcional `38;5;N`)

### UTF-8
- Decodificação incremental: se um caractere multi-byte vier cortado no fim do chunk, guardar estado parcial.

### API
- `Feed(ReadOnlySpan<byte> data)`
- `Resize(int cols, int rows)`
- `GetSnapshot()` retornando cópia thread-safe para a UI.

## Especificação técnica — Camada 4: UI (Avalonia)

### Render
- Evitar `TextBlock/TextBox` para bytes brutos.
- Preferir controle custom: herdar de `Control` e sobrescrever `Render(DrawingContext)`.
- Renderizar o snapshot (linhas/células) de forma incremental.

### Input
- Mapear teclas para bytes:
  - Enter → `\r`
  - Backspace → `0x7f`
  - Ctrl+C → `0x03`

### Resize
- Converter pixels → cols/rows baseado em `charWidth` e `lineHeight`.
- Chamar `PtySession.Resize(cols, rows)`.

### Batching
- Atualizar render no máximo 60fps, não a cada chunk recebido.

## Interface do módulo (contrato)

Sugestão de contrato (ajustar ao Core/Application da Fluence):

- `ITerminalSession`
  - `Start(shellPath, workingDirectory, cols, rows)`
  - `SendInput(ReadOnlySpan<byte>)`
  - `Resize(cols, rows)`
  - `GetCurrentSnapshot()`
  - eventos: `SessionExited`, `SnapshotUpdated`

## Critérios de aceitação

- Shell inicia **100% das vezes** (sem intermitência).
- `echo teste` imprime e permanece.
- `clear` funciona (tela limpa, prompt volta corretamente).
- Redimensionar painel atualiza `stty size`.
- `Ctrl+C` interrompe comandos longos.
- Fechar IDE encerra o shell e subprocessos (sem zombie).

## Fallback (debug rápido)

Para confirmar que o problema de flicker é ANSI/VT:

- Iniciar shell com `TERM=dumb`.
- Resultado esperado: sem cores, mas output não some.

> Este fallback não substitui o emulador VT; serve apenas para diagnóstico.
