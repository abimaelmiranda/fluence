# Project Concepts and Architectural Intent

> ⚠️ **Architecture is subject to future changes.** This document describes product concepts and design intent. For the current project structure and dependency rules, see `agent_code_rules.md` Section 3.

## Purpose
This document captures the specific product concepts and architectural decisions for the custom C# IDE.
It complements `agent_code_rules.md` by describing how the system is supposed to think and behave.
The goal is to preserve the original intent of the product while implementation evolves.

---

## 1. Product Vision
The product is a custom cross-platform IDE for C# focused on:
- Windows and macOS
- minimalism
- strong C# workflow support
- low UI overhead
- avoiding Electron-based complexity

This IDE is not trying to be a generic editor for every language.
It is a focused tool optimized for the user's personal workflow.

---

## 2. Core Product Philosophy

### 2.1 The IDE Is a Thin, Strong Shell
The IDE is a lightweight shell over the proven .NET ecosystem.
It reuses:
- Roslyn
- MSBuild
- dotnet CLI
- official Debug Adapter Protocol tooling

Post-MVP package direction:
- `Microsoft.Build.Locator` must be registered before any direct `Microsoft.Build.*` API usage.
- `Microsoft.Build` is the preferred package for safe `.csproj` inspection/editing workflows.
- `Buildalyzer.Workspaces` should be evaluated first for bridging Buildalyzer data into Roslyn workspaces.
- `Microsoft.CodeAnalysis.Workspaces.MSBuild` should be evaluated if Buildalyzer workspace integration is not sufficient for IntelliSense/LSP scenarios.

It does not reimplement:
- build systems
- solution parsing unnecessarily
- IntelliSense engines
- compilers
- debug engines

### 2.2 The Main Problem to Solve
The main problem is not the weight of Roslyn or MSBuild.
The main problem is the UI and runtime overhead of VS Code and Electron.

Therefore, the project deliberately keeps Roslyn, MSBuild, and DAP.
And replaces the Electron shell, the extension-heavy environment, and the generic multi-language editor assumptions.

### 2.3 RAM Philosophy
Modular architecture at Level 2 does not increase RAM.
Modules run in the same process.
The overhead is only DI registration, which is negligible.

What increases RAM:
- out-of-process modules (Level 3 — not used here)
- Electron
- JavaScript extension runtimes
- webviews

None of those are in this stack.

---

## 3. Architectural Style: Modular Monolith Level 2

### 3.1 What Level 2 Means
Modules are logically isolated units running in the **same process**.
There are no separate runtimes, no IPC, no out-of-process communication.

Modules:
- define their own services and handlers
- register themselves via `IIdeModule`
- communicate through shared Core contracts and Workspace state
- do not directly reference each other

### 3.2 Why Level 2 and Not Level 3
Level 3 (out-of-process) would solve crash isolation more completely, but at the cost of:
- extra processes
- more RAM per module
- IPC complexity
- latency between modules

That is exactly the overhead this project is trying to avoid.
Level 2 provides logical isolation with contained error handling at module boundaries, which is sufficient for this use case.

### 3.3 Module Map

```
Core                  ← workspace, DI contracts, module host, shell contracts
                        IShellEventBus, ModuleContributions, ShellPanelContribution
Infrastructure        ← OS/platform adapters: ProcessHost, Pty, TerminalService,
                        LSP/DAP protocol utilities, DotnetSdkProvisioningService
Desktop               ← Avalonia shell, Bootstrapper, WelcomeScreen, UI adapters
├── FileExplorer      ← filesystem tree navigation
├── SolutionView      ← .sln / .csproj parsing + Buildalyzer + ProjectReferenceService
├── Editor            ← AvaloniaEdit, tabs, TextFileService, LSP overlay, debug overlay
├── Terminal          ← integrated OS terminal (XTerm.NET + PTY via ITerminalService)
├── DotnetCli         ← build, run, test, restore, clean — dotnet CLI command handlers
├── LanguageServer    ← OmniSharp lifecycle, LSP bridge, completion, diagnostics, navigation
├── Debug             ← DAP session, breakpoints, variable evaluation, call stack
├── SourceControl     ← Git CLI integration, staging, commit, diff, branch management
├── NuGetExplorer     ← NuGet package browsing and management
├── LspSetup          ← OmniSharp binary provisioning
├── DebuggerSetup     ← netcoredbg binary provisioning
├── Settings          ← application preferences
└── Agent             ← (planned) AI agent with native workspace access
```

For full module details see `.agents/module_catalog.md`.

Each module registers its own services and declares static shell contributions such as panels. The shell knows only `IIdeModule` / `IModule` — no module concrete types leak into `Desktop`.

### 3.4 Module Lifecycle
Each module goes through:
1. `Register` — registers its services, handlers, and domain implementations into DI
2. `GetContributions` — declares static shell contributions without starting runtime work
3. `InitializeAsync` — subscribes to shell events (`IShellEventBus`), performs runtime activation, sets `ModuleState.Active`
4. Runtime — active, faulted, or disabled state

Modules expose explicit identity and order through `Id`, `DisplayName`, and `StartupOrder`. `Id` is the stable key for module state, logs, and contributions; `DisplayName` is for UI; `StartupOrder` makes startup order intentional while shutdown remains the reverse lifecycle.

### 3.4.1 Shell Events as the Action Bridge
The shell (`MainWindowViewModel`) does not know about module-specific command types. IDE-level actions (build, run, test) are dispatched as **shell request events** via `IShellEventBus`. Modules subscribe in `InitializeAsync` and handle them internally. This keeps the shell ViewModel free of all module dependencies.

### 3.5 Module State
- `Active` — running normally, panel visible
- `Faulted` — failed but contained, panel shows degraded state
- `Disabled` — intentionally off, panel hidden

A faulted module must not crash the application.
The shell catches errors at module boundaries and updates module state accordingly.

---

## 4. Workspace Is the Center of the System

### 4.1 Foundational Concept
`Workspace` is the central model of the IDE.
It is the main source of truth.
Everything important is anchored in the workspace.

### 4.2 What Workspace Owns
- current workspace mode
- current opened solution, folder, or file
- open documents and tabs
- active document
- dirty state of documents
- startup project
- build session state
- debug session state
- terminal sessions
- module states
- future agent session

### 4.3 Why This Matters
The application is not a collection of isolated screens with disconnected state.
The IDE is a living environment, and `Workspace` is its runtime model.

If multiple ViewModels or modules need the same information, that information belongs to the workspace.
Modules read from and write to the workspace through `IWorkspaceContext`.

---

## 5. Workspace Modes
The shell starts empty and changes according to what the user opens.

### 5.1 Supported Modes
- `Empty` — nothing opened, Welcome Screen shown
- `FileOnly` — a single file opened, editor-first layout
- `Folder` — a folder opened, File Explorer shown
- `Solution` — a .sln opened, Solution View shown

### 5.2 UX Implication
The visible UI reacts to workspace mode.
Empty or irrelevant panels must not appear just because the layout technically supports them.

---

## 6. Welcome Screen Is the Entry Point
The initial experience is a clean Welcome Screen.

### 6.1 Actions
- Open File
- Open Folder
- Open Solution

### 6.2 Intent
This screen keeps the application simple and predictable.
It makes workspace initialization explicit and intentional.

---

## 7. File Explorer and Solution View Are Different Concepts
This distinction is mandatory.
The system must not collapse them into the same model.

### 7.1 File Explorer
Based on the real filesystem.
Shows folders, files, and raw disk structure.
Useful for navigating everything inside an opened folder.

### 7.2 Solution View
Based on `.sln` and `.csproj` semantics.
Represents solution, projects, project items, project references, and package references.
Useful for reasoning about the C# solution structure.

### 7.3 Design Rule
File Explorer and Solution View may look similar in UI terms, but they are not the same domain concept.
They must have separate models, separate loading logic, and separate modules.

---

## 8. Minimalist Editor Experience
The editor experience stays intentionally minimal.
The goal is not feature maximalism.
The goal is a clean environment for productive coding.

### 8.1 MVP Editor Expectations
- open file
- edit file
- save file
- syntax highlighting via TextMate grammars
- tab management
- auto-save on focus lost
- multiple cursors

### 8.2 UX Principles
The editor should feel quiet, direct, responsive, and familiar to a VS Code user with a minimal setup.
The UI avoids unnecessary sidebars, popups, and chrome.

---

## 9. Tabs Are a First-Class Concept
Documents are not a temporary UI implementation detail.
The set of open tabs is part of the workspace state.

### 9.1 TabSession Concept
`TabSession` manages:
- open tabs
- active tab
- dirty state
- ordering of tabs
- future split/group information

### 9.2 Why It Matters
This keeps the IDE coherent and avoids duplicating open-document state across controls or modules.

---

## 10. Terminal Is the Output Surface for the MVP
The integrated terminal is not an accessory.
It is the main execution and output surface for the MVP.

### 10.1 Responsibilities
- build output
- run output
- test output
- user interaction with launched processes

### 10.2 Simplicity Rule
Instead of building custom output panes too early, the MVP reuses the terminal as the natural host for CLI execution.

---

## 11. dotnet CLI Is the Operational Backbone
The application treats the `dotnet` CLI as a core operational dependency.

### 11.1 MVP Commands
- `dotnet build`
- `dotnet run`
- `dotnet test`
- `dotnet restore`
- `dotnet clean`

### 11.2 Invocation Style
Actions are triggered from context menus, routed through application use cases, and executed via terminal/process abstractions.

---

## 12. Context Menus Are a Core Interaction Pattern
The Solution View exposes contextual actions directly where the user already is.

### 12.1 On Solution Nodes
- Build Solution
- Clean Solution
- Restore Solution

### 12.2 On Project Nodes
- Build
- Run
- Test
- Clean
- Restore
- Set as Startup Project

### 12.3 On File/Folder Nodes
- New File
- Rename
- Delete
- Copy Path
- Reveal in OS Explorer

---

## 13. Themeability Must Be Preserved
Themeability is a product requirement, not a cosmetic afterthought.

### 13.1 Design Intent
The user cares about themes and visual control.
The UI must remain easy to restyle and evolve.

### 13.2 Architectural Implication
Use design tokens and centralized theme resources.
Do not hardcode colors and visual constants across views.

### 13.3 Base Strategy
- Avalonia styling system
- resource dictionaries
- control themes
- Semi.Avalonia as base
- custom tokens: `Colors.axaml`, `Typography.axaml`

---

## 14. Libraries Are Chosen to Accelerate Without Excess Bloat

### 14.1 MVP Stack
- Avalonia UI
- AvaloniaEdit
- Semi.Avalonia
- Buildalyzer
- CommunityToolkit.Mvvm
- Scrutor
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging
- Serilog

### 14.2 Selection Principle
A library is welcome when it reduces implementation time, is mature, matches architecture boundaries, and does not force unnecessary complexity.

---

## 15. The Future Agent Is a Native Part of the Vision
Even if not part of the MVP, the architecture must leave room for an Agent module.

### 15.1 Why
A custom IDE can expose structured internal state to the agent far better than a generic plugin can.

Future native access examples:
- active document
- current solution
- startup project
- selected project
- build state
- debug state

### 15.2 Architectural Consequence
Do not design the system as if AI/agent integration were impossible.
Keep workspace and application state explicit and accessible through `IWorkspaceContext`.

---

## 16. Implementation Status

### 16.1 Implemented
- Welcome Screen
- Open File / Folder / Solution
- File Explorer module
- Solution View module (Buildalyzer, project references, NuGet packages)
- File editing and saving with dirty state tracking
- TextMate syntax highlighting
- Integrated terminal module (XTerm.NET + PTY, TUI-capable)
- dotnet CLI integration module (build/run/test/restore/clean)
- Tab management
- Auto-save on focus lost
- Language Server / OmniSharp (completion, hover, diagnostics, go-to-definition, signature help, code actions)
- Debugger / netcoredbg (DAP integration, breakpoints, variable evaluation)
- Source Control (Git CLI, staging, commit, diff, branch management)
- NuGet Explorer
- LspSetup and DebuggerSetup provisioning modules
- Settings module
- Theming system (design tokens, JSON themes)

### 16.2 Planned
- Agent module with native workspace access
- Plugin marketplace / extensibility system
- Windows terminal (ConPTY) full validation
- Linux support

---

## 17. Non-Goals
The project is not trying to:
- clone Visual Studio completely
- become a general-purpose IDE for every language
- front-load all advanced features before basic workflow is solid
- optimize away Roslyn/MSBuild at the cost of robustness
- use out-of-process modules that increase RAM and complexity

---

## 18. Implementation Heuristic
When implementation choices are unclear, prefer the option that best preserves these ideas:

1. Workspace is the center.
2. UI reacts to workspace mode.
3. File Explorer and Solution View are distinct concepts and distinct modules.
4. Modules communicate through Core contracts and Workspace, never directly.
5. dotnet CLI is the main operational path in the MVP.
6. The terminal is the default output surface.
7. Minimalism is a feature, not a limitation.
8. Themeability must stay easy.
9. Reuse strong ecosystem tools instead of reinventing them.
10. Level 2 modular monolith: same process, logical isolation, error containment at boundaries.

---

## 19. Review Questions for Future Changes
Use these questions when reviewing new code or features:

- Does this strengthen or weaken Workspace as the source of truth?
- Is this introducing UI complexity without workflow value?
- Is this collapsing Solution View and File Explorer into one concept?
- Is this reimplementing something the .NET ecosystem already does well?
- Is this making theming harder?
- Is this adding generic editor complexity that does not help the C# workflow?
- Is this bypassing the module system and creating a direct cross-module dependency?
- Is this still aligned with the MVP and product philosophy?
- Does this increase RAM without a justified reason?
