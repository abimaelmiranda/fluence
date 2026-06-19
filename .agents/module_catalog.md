# Module Catalog

This document describes every module currently implemented in Fluence, their responsibilities, and key files. Use this as the reference for understanding what exists before adding or modifying functionality.

---

## src/ — Core Framework

### Fluence.Core
The shared kernel. Owns contracts, workspace state, and shell abstractions.

Key types:
- `IIdeModule` — contract every module must implement
- `IModuleHost` — provided to modules during `InitializeAsync`
- `IWorkspaceContext` — read/write access to Workspace state
- `Workspace` — single source of truth for all IDE runtime state
- `WorkspaceMode` — Empty / FileOnly / Folder / Solution
- `TabSession` / `OpenDocument` — tab and document state
- `IShellEventBus` — publish/subscribe channel for cross-module events
- `ModuleContributions` / `ShellPanelContribution` — static shell contribution declaration
- `IViewRegistry` — shell ViewModel→View mapping where needed
- `ICommandHandler<T>` / `IQueryHandler<T,R>` — internal use-case contracts
- `ITerminalService` / `IProcessHost` — platform process contracts
- `IStartupCoordinator` — ordered, async startup contract
- `IShutdownCoordinator` — ordered, bounded module shutdown contract
- `IProcessSpawner` / `ITrackedProcess` — tracked long-lived process ownership contract
- Shell request events: `BuildWorkspaceRequestedEvent`, `RunProjectRequestedEvent`, `TestWorkspaceRequestedEvent`, `RestoreWorkspaceRequestedEvent`, `CleanWorkspaceRequestedEvent`
- `FluenceException` — base for all module domain exceptions

### Fluence.Infrastructure
OS/platform adapters only. Implements Core contracts.

Key implementations:
- `ProcessHost` — safe process execution wrapper
- `ProcessSpawner` — tracked process start/kill/session metadata cleanup
- `TerminalService` — terminal session management
- `Pty/` — `Porta.Pty` as the active Unix PTY adapter, `WindowsPtySession` (ConPTY)
- process tracking — persists owned process metadata for best-effort cleanup after crash
- `Protocols/Lsp/` — LSP protocol utilities
- `Protocols/Dap/` — DAP protocol utilities (`DapClient`, `NetcoredbgToolService`)
- `DotnetSdkProvisioningService` — SDK discovery and provisioning

### Fluence.Desktop
Avalonia shell. Bootstraps and hosts modules. Owns no domain logic.

Key files:
- `Bootstrapper.cs` — module registration and DI composition root
- `ApplicationStartupCoordinator.cs` — ordered startup loader for cleanup, contributions, and module activation
- `MainWindow.axaml/.cs` — shell layout
- `MainWindowViewModel.cs` — shell state, panel hosting, workspace mode reactions
- `ViewLocator.cs` — resolves ViewModel→View via `IViewRegistry`
- `Themes/` — `Colors.axaml`, `Typography.axaml`, control themes
- `Views/WelcomeScreenView.axaml` — entry point when workspace is Empty

---

## modules/ — Feature Modules

All modules implement `IIdeModule`. They reference only Core, never Infrastructure or each other (except via acyclic extension pattern).

### Fluence.Modules.Editor
Code editing surface. The central module.

Responsibilities:
- File open, edit, save via AvaloniaEdit
- TextMate syntax highlighting (TextMateSharp.Grammars)
- Auto-save on focus loss
- Dirty state tracking
- Multiple cursors
- Tab management (drives `TabSession` in Workspace)
- LSP overlay: completion, hover, signature help, diagnostics, go-to-definition
- Debug overlay: breakpoint gutter, evaluation hover, current line indicator

Key files: `EditorView.axaml.cs` (split into 9 partial files), `EditorViewModel.cs`, `TextFileService.cs`

### Fluence.Modules.Terminal
Integrated interactive terminal.

Responsibilities:
- XTerm.NET-based VT100/ANSI rendering
- PTY bridge (macOS: posix_spawn; Windows: ConPTY) via `ITerminalService`
- Interactive shell (zsh on macOS, cmd/pwsh on Windows)
- Resize handling (PTY + XTerm sync)
- Scrollback and TUI app support
- Process output routing (build, run, test output from DotnetCli)
- Shutdown terminates the integrated terminal process group/tree through the PTY adapter

Key files: `Terminal/TerminalControl.cs`, `ViewModels/TerminalSessionViewModel.cs`, `Views/TerminalView.axaml.cs`

See `.agents/fluence_terminal_pty_spec.md` for architecture and invariants.

### Fluence.Modules.SolutionView
.NET solution model and project browser.

Responsibilities:
- `.sln`/`.slnx` parsing via Buildalyzer
- Project and file tree display
- Project references (view, add, remove)
- NuGet package references
- Startup project selection
- Context menus: Build / Run / Test / Clean / Restore per solution and project nodes

### Fluence.Modules.FileExplorer
Raw filesystem navigation. Distinct from Solution View (different domain concept).

Responsibilities:
- Filesystem tree (folders and files)
- Expand/collapse
- File open on double-click
- Context menu actions (new file, rename, delete, copy path, reveal in Finder/Explorer)

### Fluence.Modules.DotnetCli
dotnet CLI command dispatcher.

Responsibilities:
- Handles shell request events from Core: Build, Run, Test, Restore, Clean
- Constructs CLI commands safely
- Routes output through `ITerminalService`

### Fluence.Modules.LanguageServer
OmniSharp LSP integration.

Responsibilities:
- OmniSharp process lifecycle (start, stop, restart)
- LSP JSON-RPC protocol bridge
- Semantic tokens
- Diagnostics (errors, warnings)
- Code completion
- Hover documentation
- Signature help
- Go-to-definition navigation
- Quick fix / code actions

### Fluence.Modules.Debug
Debug Adapter Protocol (DAP) integration.

Responsibilities:
- DAP session lifecycle via `DapClient` (Infrastructure)
- Breakpoint management
- Variable and expression evaluation
- Call stack navigation
- Debug state in Workspace (active/paused/stopped)

### Fluence.Modules.SourceControl
Git integration via CLI.

Responsibilities:
- File change status tracking
- Staging and unstaging files
- Committing
- Branch management
- Stash operations
- Diff viewing

### Fluence.Modules.NuGetExplorer
NuGet package management UI.

Responsibilities:
- Package search and browsing
- Package installation, update, removal per project
- Feeds configuration

### Fluence.Modules.LspSetup
OmniSharp provisioning.

Responsibilities:
- Downloading and managing OmniSharp binaries
- Version management
- Setup UI/status

### Fluence.Modules.DebuggerSetup
netcoredbg provisioning.

Responsibilities:
- Downloading and managing netcoredbg binaries
- Version management
- Setup UI/status

### Fluence.Modules.Settings
Application preferences UI.

Responsibilities:
- Theme selection
- Font configuration
- Editor preferences
- Keybindings

---
