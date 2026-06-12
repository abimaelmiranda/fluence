# MVP Implementation Roadmap

## Purpose
This document is the practical starting point for the implementation agent.
It explains how to bootstrap the solution, what to build first, what order to follow, and how to avoid getting lost.

The agent must prioritize delivering a working vertical slice before adding sophistication.

---

# Progress Tracker

- [x] Phase 0 - Foundation
- [x] Phase 1 - Core Platform
- [x] Phase 2 - Desktop Shell
- [x] Phase 3 - Welcome Screen
- [x] Milestone 2 - Open Workspace Real
- [x] Milestone 3 - Editor Básico com Arquivos de Texto
- [x] Phase 4 follow-up - Auto Save
- [x] Phase 4 follow-up - Syntax Highlighting
- [x] Phase 6 - File Explorer Module
- [x] Phase 7 - Solution View Module
- [x] Phase 8 - Terminal Module
- [x] Phase 9 - Infrastructure Layer
- [x] Phase 10 - DotnetCLI Module
- [x] Modular Architecture Refactor (IViewRegistry, IShellEventBus, IPanelDescriptor)
- [x] MainWindow data-driven via ContentControl + ViewLocator (no module types in shell)
- [x] Application layer merged into Core; modules own their handlers and domain impls
- [x] Bootstrapper decoupled: only registers shell, platform adapters, and the module array

---

# Phase 0 - Foundation

## Goal
Create the solution structure and ensure the application boots.

## Create Solution

```bash
mkdir MyIde
cd MyIde

dotnet new sln -n MyIde
```

## Create Projects

```bash
dotnet new avalonia.app -n MyIde.Desktop

dotnet new classlib -n MyIde.Core
dotnet new classlib -n MyIde.Domain
dotnet new classlib -n MyIde.Application
dotnet new classlib -n MyIde.Infrastructure

dotnet new classlib -n MyIde.Modules.FileExplorer
dotnet new classlib -n MyIde.Modules.SolutionView
dotnet new classlib -n MyIde.Modules.Editor
dotnet new classlib -n MyIde.Modules.Terminal
dotnet new classlib -n MyIde.Modules.DotnetCli
```

## Add Projects To Solution

Add all projects to the solution.

## Configure References

### Core
No project references.

### Infrastructure
References:
- Core

### Desktop
References:
- Core
- Infrastructure
- All module projects (via IIdeModule only — no concrete module types)

### Modules
References:
- Core only

Never reference Infrastructure directly from a module — use Core contracts via DI.
Never reference another module directly.

---

# Phase 1 - Core Platform

## Goal
Create the architectural skeleton.

## Create Workspace

Important classes:

```text
Workspace
WorkspaceMode
TabSession
OpenDocument
ModuleState
```

## Create WorkspaceMode

```csharp
public enum WorkspaceMode
{
    Empty,
    FileOnly,
    Folder,
    Solution
}
```

## Create ModuleState

```csharp
public enum ModuleState
{
    Active,
    Faulted,
    Disabled
}
```

## Create Core Contracts

```text
IIdeModule
IModuleHost
IWorkspaceContext
ICommandHandler<T>
IQueryHandler<TQuery, TResult>
```

These contracts become the foundation of the application.

---

# Phase 2 - Desktop Shell

## Goal
Build the empty IDE shell.

## Create Main Window

Layout goal:

```text
macOS
+------------------------------------------+
| [Traffic Lights] Fluence IDE  [Toolbar]  |
+------------------------------------------+
|                          |               |
|   Editor Area            |   Sidebar     |
|   (main area)            |   (right)     |
|                          |               |
+------------------------------------------+
|   Terminal / Output                      |
+------------------------------------------+
```

```text
Windows
+------------------------------------------+
| File  View  Run  [Title]                 |
+------------------------------------------+
|                          |               |
|   Editor Area            |   Sidebar     |
|   (main area)            |   (right)     |
|                          |               |
+------------------------------------------+
|   Terminal / Output                      |
+------------------------------------------+
```

Important layout rules:

- Editor is the primary area and stays on the left/center.
- Sidebar lives on the RIGHT.
- Terminal lives at the bottom.
- There is no fixed left panel.
- Sidebar can be empty at first.
- Editor area can be placeholder at first.
- Terminal can be placeholder at first.

Focus only on layout first.

## Theme Setup

Install:

- Semi.Avalonia

Create:

```text
Themes/
  Colors.axaml
  Typography.axaml
```

Do not hardcode colors.

## macOS Native Top Bar Integration

On macOS, the top menu must integrate with the native system menu bar next to the Apple menu.
Do not render a fake in-window menu bar for macOS.

Use:

- `NativeMenu` for File, Edit, View, Run and other app menus.
- `ExtendClientAreaToDecorationsHint = true`
- `ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome`

Goal on macOS:

- The app menu appears in the system top bar.
- The window content blends naturally with the native title bar.
- Traffic lights remain native.
- Toolbar actions may live inside the title bar area if needed.

Goal on Windows:

- Use a simple custom top bar with menu + title.

Important:

- macOS should feel native first.
- Windows can use the custom shell layout.
- Do not force the same top bar strategy across platforms.

---

# Phase 3 - Welcome Screen

## Goal
Make the application usable.

Create Welcome Screen.

Buttons:

- Open File
- Open Folder
- Open Solution

Actions update WorkspaceMode.

Rules:

- Empty => Welcome Screen
- FileOnly => Editor
- Folder => File Explorer
- Solution => Solution View

WorkspaceMode controls the UI.

---

# Phase 4 - Editor Module

## Goal
Open and edit files.

- [x] Install:

- AvaloniaEdit

- [x] Create:

```text
EditorModule
EditorView
EditorViewModel
```

Capabilities:

- [x] Open file
- [x] Display content
- [x] Edit content
- [x] Save content
- [x] Guard unsupported binary/complex files

## Dirty State

OpenDocument must track:

```csharp
bool IsDirty
```

## Auto Save

- [ ] Implement:

Save on focus lost.

## Syntax Highlighting

- [ ] Use TextMate grammars.

No IntelliSense yet.

---

# Phase 5 - Tab Management

## Goal
Support multiple open files.

Create:

```text
TabSession
```

Tracks:

- Open tabs
- Active tab
- Dirty state

Capabilities:

- [x] Open tab
- [x] Close tab
- [x] Switch tab

Future split support is not required.

---

# Phase 6 - File Explorer Module

## Goal
Browse folders.

Create:

```text
FileExplorerModule
FileExplorerView
FileExplorerViewModel
```

Capabilities:

- [ ] Expand folder
- [ ] Collapse folder
- [ ] Open file on double click

Source:

Filesystem.

Not solution aware.

---

# Phase 7 - Solution View Module

## Goal
Browse .NET solutions.

- [x] Install:

- Buildalyzer

- [x] Create:

```text
SolutionViewModule
SolutionViewView
SolutionViewViewModel
```

Capabilities:

- [x] Load .sln / .slnx
- [x] Show projects
- [x] Show files
- [x] Show project references
- [x] Show package references
- [x] Show `Dependencies > Projects`
- [x] Show `Dependencies > Packages`

Project System básico:

- [x] Load project references from `.csproj`
- [x] Resolve project references against projects in the opened solution
- [x] Preserve unresolved project references as path/name nodes
- [x] Load package references with version when available
- [x] Add project references through an `Add Reference` modal
- [x] Mark existing project references as checked in the modal
- [x] Remove project references through context menu confirmation
- [x] Edit `.csproj` directly with structured XML
- [x] Prevent duplicate references
- [x] Prevent self references
- [x] Prevent simple direct circular references
- [x] Normalize Windows-style project reference paths on macOS/Linux
- [x] Reload Solution View after `.csproj` reference changes
- [x] Set Startup Project from project context menu
- [x] Show startup project indicator in Solution View
- [x] Run uses startup project in solution mode when selected

NuGet Explorer follow-up:

- [ ] Support private package sources / private feeds
- [ ] Support `Directory.Packages.props` / Central Package Management

Important:

Solution View is NOT File Explorer.

Separate models.

---

# Phase 8 - Terminal Module

## Goal
Provide command output.

Create:

```text
TerminalModule
TerminalView
TerminalViewModel
```

Capabilities:

- [x] Display process output
- [x] Display process errors
- [x] Use an append-only transcript model for MVP stability
- [x] Execute commands through a non-interactive shell process
- [x] Strip ANSI/control sequences instead of emulating a full xterm
- [x] Support explicit clear via `clear` / `cls`
- [x] Support process cancellation through Stop / terminal service cancellation
- [x] Start collapsed by default; expand when Dotnet CLI commands run

Terminal becomes the primary output surface.

No custom output panes.

MVP terminal decision:

- The MVP terminal is intentionally a stable command console, not a full PTY/xterm emulator.
- Output must be append-only unless the app or an explicit `clear` / `cls` command clears it.
- Full-screen/interactive apps such as `vim`, `top`, complex REPLs, and prompt-driven programs are out of scope for MVP.
- Keep `IPtyHost` / `IPtySession` available for a future terminal implementation, but do not use PTY as the default MVP terminal path.

Post-MVP terminal TODO:

- [ ] Evaluate `XTerm.NET` as the native C#/Avalonia terminal emulator path.
- [ ] Prototype `XTerm.NET` + `IPtySession` behind the existing terminal contracts.
- [ ] Build or adopt an Avalonia renderer for the `XTerm.NET` buffer.
- [ ] Validate resize, scrollback, alternate buffer, Unicode/wide chars, colors, clipboard, keyboard input, and process lifecycle.
- [ ] Only replace the transcript terminal after the interactive terminal is demonstrably stable.

---

# Phase 9 - Infrastructure Layer

## Goal
Create safe wrappers.

- [ ] Create:

```text
IProcessHost
ITerminalService
IFileSystem
```

- [ ] Implement:

```text
ProcessHost
TerminalService
FileSystemService
```

No Process.Start outside Infrastructure.

---

# Phase 10 - Dotnet CLI Module

## Goal
Run dotnet commands.

- [ ] Create:

```text
BuildSolutionCommand
RunProjectCommand
TestProjectCommand
RestoreCommand
CleanCommand
```

Context menus trigger commands.

Commands execute through:

```text
ITerminalService
IProcessHost
```

Output goes to terminal.

---

# Phase 11 - Context Menus

## Solution Menu

- [x] Build
- [x] Restore
- [x] Clean

## Project Menu

- [x] Build
- [x] Run
- [x] Test
- [x] Restore
- [x] Clean
- [x] Set Startup Project
- [x] Add Project Reference

## Project Reference Menu

- [x] Remove

## File Menu

- [ ] Rename
- [ ] Delete
- [ ] Copy Path
- [ ] Reveal in Explorer/Finder

---

# Phase 12 - Polish

## Validate

- [x] Open File
- [x] Open Folder
- [x] Open Solution
- [x] Open multiple tabs
- [x] Edit text file
- [x] Save text file
- [x] Reject unsupported binary/complex files
- [x] Auto save
- [x] Syntax highlighting
- [x] Build
- [x] Run
- [x] Test
- [x] Terminal output

Application must be stable.

No IntelliSense.
No Debugger.
No Git.

---

# Architectural Reminders

Always remember:

1. Workspace is the source of truth.
2. Modules never reference each other directly.
3. Solution View and File Explorer are different concepts.
4. Terminal is the output surface.
5. Dotnet CLI is the operational backbone.
6. Themeability is mandatory.
7. Simplicity beats cleverness.
8. Keep Roslyn/MSBuild; remove Electron complexity.

---

# Master TODO List

## Foundation

- [X] Create solution
- [X] Create projects
- [X] Configure references
- [X] Configure DI
- [ ] Configure logging

## Core

- [X] Create Workspace
- [X] Create WorkspaceMode
- [X] Create ModuleState
- [X] Create IIdeModule
- [X] Create IModuleHost
- [X] Create IWorkspaceContext

## Desktop Shell

- [X] Create MainWindow layout
- [X] Configure Semi.Avalonia
- [X] Create theme tokens

## Welcome Screen

- [X] Create Welcome Screen
- [X] Open File flow
- [X] Open Folder flow
- [X] Open Solution flow

## Editor Module

- [X] Install AvaloniaEdit
- [X] Open file
- [X] Edit file
- [X] Save file
- [X] Dirty state
- [X] Auto save
- [X] Syntax highlighting

## Tabs

- [X] Create TabSession
- [X] Open tabs
- [X] Close tabs
- [X] Switch tabs

## File Explorer

- [X] Create tree model
- [X] Folder navigation
- [X] Open file from explorer

## Solution View

- [ ] Install Buildalyzer
- [ ] Load solution
- [ ] Show projects
- [ ] Show files
- [ ] Show references
- [ ] Post-MVP: add `Microsoft.Build.Locator` before using direct MSBuild APIs
- [ ] Post-MVP: add `Microsoft.Build` for safe `.csproj` inspection/editing
- [ ] Post-MVP: evaluate `Buildalyzer.Workspaces` as the first Roslyn workspace bridge
- [ ] Post-MVP: evaluate `Microsoft.CodeAnalysis.Workspaces.MSBuild` if Buildalyzer workspace integration is not enough
- [ ] NuGet Explorer: support private package sources / private feeds
- [ ] NuGet Explorer: support `Directory.Packages.props` / Central Package Management

## Terminal

- [X] Create terminal panel
- [X] Show stdout
- [X] Show stderr
- [X] Replace PTY/xterm-like grid with stable transcript console for MVP
- [X] Execute typed commands through `ITerminalService`
- [X] Keep terminal collapsed on startup
- [X] Expand terminal automatically for Dotnet CLI commands
- [X] Support explicit transcript clear via `clear` / `cls`
- [X] Add process cancellation path
- [ ] Post-MVP: evaluate `XTerm.NET` for a real native interactive terminal
- [ ] Post-MVP: prototype `XTerm.NET` renderer in Avalonia
- [ ] Post-MVP: connect `XTerm.NET` to `IPtyHost` / `IPtySession`

## Infrastructure

- [X] IProcessHost
- [X] ProcessHost
- [X] ITerminalService
- [X] TerminalService
- [ ] IFileSystem
- [ ] FileSystemService

## Dotnet CLI

- [X] Build
- [X] Run
- [X] Test
- [X] Restore
- [X] Clean

## Context Menus

- [ ] Solution menu
- [ ] Project menu
- [ ] File menu

## MVP Validation

- [X] Open File works
- [X] Open Folder works
- [ ] Open Solution works
- [X] Tabs work
- [X] Auto-save works
- [X] Syntax highlighting works
- [X] Build works
- [X] Run works
- [X] Test works
- [X] Terminal works
- [ ] No architecture violations
- [ ] MVP complete
