# Fluence

A lightweight, native C# IDE for macOS and Windows — built with Avalonia UI.

Fluence is a focused desktop IDE designed for C# developers who want fast startup, low memory usage, and a clean workflow without the overhead of web-based editors. It is a thin shell over the proven .NET ecosystem: Roslyn, MSBuild, DAP, and the dotnet CLI — nothing reimplemented from scratch.

---

## Features

- **Code Editor** — AvaloniaEdit with TextMate syntax highlighting, multiple cursors, auto-save, and dirty state tracking
- **Integrated Terminal** — Full PTY-backed terminal (zsh/cmd) via XTerm.NET with scroll, resize, and TUI app support
- **Solution View** — .sln/.slnx parsing, project tree, project references, NuGet packages, and context menu actions (build, run, test, clean)
- **File Explorer** — Filesystem tree navigation independent of the solution model
- **Language Server (OmniSharp)** — Code completion, hover, signature help, diagnostics, go-to-definition, and semantic highlighting
- **Debugger (DAP/netcoredbg)** — Debug Adapter Protocol integration with breakpoints, variable evaluation, and call stack
- **Source Control** — Git integration via CLI (status, stage, commit, branch management, diff)
- **NuGet Explorer** — Package browsing and management per project
- **dotnet CLI** — Build, run, test, restore, and clean — routed through the terminal as the default output surface
- **Theming** — Design-token-based theme system with JSON theme support and live reload

---

## Architecture

Fluence follows a **Modular Monolith** architecture: all modules run in a single process with logical isolation. Modules communicate only through shared Core contracts and Workspace state — never directly.

```
Core              ← workspace, DI contracts, shell contracts (IIdeModule, IWorkspaceContext,
                    IShellEventBus, IPanelDescriptor, IViewRegistry, ITerminalService)
Infrastructure    ← OS/platform adapters: ProcessHost, Pty (macOS/Windows), TerminalService
Desktop           ← Avalonia shell: App, MainWindow, Bootstrapper, WelcomeScreen, UI services

modules/
├── Editor          ← AvaloniaEdit, tabs, TextMate grammars, LSP overlay, debug overlay
├── Terminal        ← XTerm.NET renderer, PTY bridge, shell lifecycle
├── SolutionView    ← .sln/.csproj parsing via Buildalyzer, project tree, context menus
├── FileExplorer    ← filesystem tree navigation
├── DotnetCli       ← build/run/test/restore/clean command handlers
├── LanguageServer  ← OmniSharp lifecycle, LSP protocol bridge, semantic tokens
├── Debug           ← DAP integration, breakpoints, variable evaluation
├── SourceControl   ← Git CLI integration, file status, staging, commit, diff
├── NuGetExplorer   ← NuGet package browsing and management
├── LspSetup        ← OmniSharp provisioning and version management
├── DebuggerSetup   ← netcoredbg provisioning and version management
└── Settings        ← Application preferences
```

**Workspace** is the single source of truth. The UI reacts to workspace mode:
- `Empty` → Welcome Screen
- `FileOnly` → Editor-first layout
- `Folder` → File Explorer + Editor
- `Solution` → Solution View + Editor

---

## Tech Stack

| Area | Library |
|---|---|
| UI Framework | [Avalonia](https://avaloniaui.net/) 12.0 |
| UI Components | [Semi.Avalonia](https://github.com/irihitech/Semi.Avalonia) |
| Code Editor | [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) + TextMate grammars |
| Terminal Emulator | [XTerm.NET](https://github.com/nickogl/xterm.net) |
| PTY (pseudo-terminal) | [Porta.Pty](https://github.com/nickogl/porta.pty) |
| Language Server | [OmniSharp](https://github.com/OmniSharp/omnisharp-roslyn) |
| Debugger | [netcoredbg](https://github.com/Samsung/netcoredbg) |
| Solution Parsing | [Buildalyzer](https://github.com/daveaglick/Buildalyzer) |
| UI Icons | [Material.Icons.Avalonia](https://github.com/AvaloniaUtils/Material.Icons.Avalonia) |
| File/Folder Icons | [Material Icon Theme](https://github.com/PKief/vscode-material-icon-theme) (vendored) |
| MVVM | [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) |
| Logging | [Serilog](https://serilog.net/) |

---

## Releases

Currently, an alpha build is available only for macOS on Apple Silicon in the repository's Releases section.

For other platforms, Fluence must be built from source.

We plan to provide additional platform-specific releases in the future.

## Building From Source

First you need to clone the repo
```bash
git clone https://github.com/abimaelmiranda/fluence.git
```

Requires .NET 10 SDK.


```bash
# Restore
dotnet restore

# Build
dotnet build

# Publish for macOS (creates .app bundle)
dotnet publish src/Fluence.Desktop/Fluence.Desktop.csproj -r osx-arm64 -c Release

# Publish for Windows
dotnet publish src/Fluence.Desktop/Fluence.Desktop.csproj -r win-x64 -c Release
```

The macOS publish target automatically creates a proper `.app` bundle with `Info.plist` and `.icns` icon.

---

## Platform Status

| Platform | Status |
|---|---|
| macOS (Apple Silicon) | Primary development target — fully functional |
| macOS (Intel) | Supported |
| Windows | Builds and runs; terminal (ConPTY) implemented, not fully validated |
| Linux | Not tested |

---

## Acknowledgments

Fluence would not exist without these open source projects:

- **[Avalonia](https://avaloniaui.net/)** — The cross-platform UI framework that makes native .NET desktop apps possible without Electron.
- **[XTerm.NET](https://github.com/nickogl/xterm.net)** — VT100/ANSI terminal emulator that powers the integrated terminal.
- **[Porta.Pty](https://github.com/nickogl/porta.pty)** — PTY abstraction layer enabling real interactive shell sessions on macOS and Windows.
- **[netcoredbg](https://github.com/Samsung/netcoredbg)** — The .NET debugger backend implementing the Debug Adapter Protocol.
- **[OmniSharp](https://github.com/OmniSharp/omnisharp-roslyn)** — The Roslyn-based language server providing IntelliSense and diagnostics for C#.
- **[Material Icon Theme](https://github.com/PKief/vscode-material-icon-theme)** — File and folder icons used throughout the IDE (MIT license, vendored in `third_party/`).
