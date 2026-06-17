# Agent Code Rules

## Purpose
This document defines the mandatory coding rules for the agent implementing the custom C# desktop IDE.
The goal is to preserve a strict architecture, maintainability, and performance while avoiding common mistakes in desktop and modular development.

---

## 1. Core Architectural Style

The solution follows:
- **Modular Monolith**
- **Ports and Adapters / Hexagonal Architecture**
- **MVVM** in the desktop layer
- **DDD-light** in the domain layer

Modules are logically isolated assemblies or well-bounded namespaces running in the **same process**.
There are no out-of-process modules, no IPC, no separate runtimes.

---

## 2. Module System Rules

### 2.1 Every Feature Is a Module
Features must be organized as modules.
Each module is a self-contained unit that:
- defines its own services
- registers its own handlers
- exposes its own UI panels (if applicable)
- does not reach into other modules directly

### 2.2 The IIdeModule Contract
Every module must implement `IIdeModule`:

```csharp
public interface IModule
{
    string Name { get; }
    void Register(IServiceCollection services);
    void RegisterViews(IViewRegistry registry) { }
    IReadOnlyList<IPanelDescriptor> GetPanelDescriptors() => [];
    void Initialize(IModuleHost host);
}
```

- `Register` — registers all module-owned services, handlers, and implementations into the DI container.
- `RegisterViews` — registers ViewModel → View type mappings in `IViewRegistry`.
- `GetPanelDescriptors` — declares panels this module contributes to the shell (slot, visibility, order).
- `Initialize` — post-DI startup logic (sets `ModuleState.Active` when ready).

No module should bypass this contract and self-register in an ad hoc way.

### 2.3 Module Registration at Startup
Modules are registered explicitly at startup in a single place (`Bootstrapper.cs`).
The order of registration must be intentional.

```csharp
var modules = new IIdeModule[]
{
    new FileExplorerModule(),
    new SolutionViewModule(),
    new EditorModule(),
    new TerminalModule(),
    new DotnetCliModule(),
};

foreach (var module in modules)
{
    module.Register(services);
    module.RegisterViews(viewRegistry);
}
```

Do not scatter module registration across the codebase.

### 2.4 Module Boundaries and Allowed Extensions

The default rule is: modules do not reference each other.
Cross-module communication goes through:
- shared contracts defined in Core
- workspace state via `IWorkspaceContext`
- shell events via `IShellEventBus`

**Exception — submodules / extensions:** a module may reference another module's public types when it is explicitly extending that module's functionality, provided the dependency is:
- **Acyclic**: if A → B, then B must not reference A in any form
- **Unidirectional**: the base module is completely unaware of its extensions
- **Surface-limited**: the extending module only consumes types from the base module's declared public API (e.g. a `Contracts/` folder); it must not inject into or call the base module's internal services

Example: `Fluence.Modules.Agent.SolutionBridge` may reference `Fluence.Modules.SolutionView` to read `SolutionTreeNode` — SolutionView has zero knowledge of Agent.

### 2.5 Module State
Each module must support a `ModuleState`:
- `Active` — running normally
- `Faulted` — failed but contained
- `Disabled` — intentionally off

The UI must react to module state.
A faulted module should show a degraded panel, not crash the application.

### 2.6 Error Isolation at Module Boundaries
Module calls from the Core shell must be wrapped with error handling:

```csharp
try
{
    await module.InitializeAsync();
}
catch (Exception ex)
{
    _logger.LogError(ex, "Module {Name} failed to initialize.", module.Name);
    _workspace.SetModuleState(module.Name, ModuleState.Faulted);
}
```

Never let an unhandled module exception propagate to the main shell uncontrolled.

### 2.7 Active Modules
All of the following modules are implemented and registered:
- `Core` — workspace, DI contracts, shell, module host
- `FileExplorer` — filesystem tree navigation
- `SolutionView` — .sln and .csproj parsing, project tree, context menus
- `Editor` — AvaloniaEdit, tabs, auto-save, syntax highlighting, LSP overlay, debug overlay
- `Terminal` — integrated OS terminal (XTerm.NET + PTY)
- `DotnetCli` — build, run, test, restore, clean
- `LanguageServer` — OmniSharp lifecycle, LSP bridge, completion, diagnostics, navigation
- `Debug` — DAP session, breakpoints, variable evaluation, call stack
- `SourceControl` — Git CLI integration, staging, commit, diff
- `NuGetExplorer` — NuGet package browsing and management
- `LspSetup` — OmniSharp binary provisioning
- `DebuggerSetup` — netcoredbg binary provisioning
- `Settings` — application preferences

See `.agents/module_catalog.md` for full details on each module's responsibilities and key files.

---

## 3. Project Responsibilities

### 3.1 Project Structure

```
src/
  Fluence.Core          ← genuinely shared contracts only:
                          IIdeModule / IModuleHost / IPanelDescriptor / IViewRegistry / IShellEventBus
                          IWorkspaceContext / Workspace / WorkspaceMode / TabSession / OpenDocument
                          ICommandHandler<T> / IQueryHandler<T,R>
                          ITerminalService / IProcessHost / IPtyHost  (cross-module platform contracts)
                          IWorkspaceDialogService / IUserNotificationService  (cross-cutting ports)
                          FluenceException  (base for all module domain exceptions)
                          Shell request events (BuildWorkspaceRequestedEvent, RunProjectRequestedEvent…)
  Fluence.Infrastructure ← ONLY OS/platform adapters: ProcessHost, Pty (Mac/Win), TerminalService
  Fluence.Desktop        ← Avalonia shell: App, MainWindow, Bootstrapper, WelcomeScreen,
                          Avalonia UI-adapter services (dialogs, notifications)

modules/
  Fluence.Modules.*     ← fully self-contained:
                          ViewModel + View + domain types + command DTOs + handlers + exceptions
                          registered entirely through IIdeModule
```

**Core owns nothing domain-specific.** Types like `SolutionTreeNode`, `ITextFileService`, dotnet CLI command DTOs belong to their modules. To add a new module: create a project → reference Core → implement `IIdeModule` → add to `Bootstrapper`. Zero `src/` files change.

### 3.1.1 Exception Hierarchy

All module-specific exceptions inherit from `FluenceException` (defined in Core):

```csharp
// Core
public abstract class FluenceException : Exception
{
    protected FluenceException(string message) : base(message) { }
    protected FluenceException(string message, Exception inner) : base(message, inner) { }
}

// SolutionView module
public sealed class SolutionWorkspaceLoadException : FluenceException { ... }

// Editor module
public sealed class UnsupportedTextFileException : FluenceException { ... }
```

The shell catches `FluenceException` for user-friendly error display without knowing concrete module types.

### 3.2 Dependency Direction

Allowed:
- `Desktop → Core`
- `Desktop → Infrastructure`
- `Desktop → Modules.*` (only via `IIdeModule` — not concrete module types)
- `Infrastructure → Core`
- `Modules.* → Core`
- `Modules.Extension → Modules.Base` (acyclic, unidirectional extension only — see 2.4)

Forbidden:
- `Core → any layer above it`
- `Infrastructure → Desktop, Modules`
- `Modules.A ↔ Modules.B` (bidirectional or cyclic cross-module reference)
- `Modules.* → Infrastructure` (use Core contracts via DI instead)

---

## 3.3 Shell Architecture

- `IViewRegistry` maps ViewModel → View types. Modules call `RegisterViews(IViewRegistry)` to register their pairs. `ViewLocator` resolves views from the registry — no reflection over namespaces.
- `IShellEventBus` is the cross-module event channel. Modules publish; the shell and other modules subscribe. Shell-level actions (build, run, test) are dispatched as **shell request events** defined in Core — `MainWindowViewModel` publishes them without knowing any module-specific command type.
- `IPanelDescriptor` declares a module panel (Slot, VisibilityMode, Order, DisplayLabel, ResolveViewModel). The shell uses this to build `ShellPanelViewModel` wrappers.
- `MainWindowViewModel` holds no module-specific types — it receives `ShellPanelViewModel` objects from `Bootstrapper.InitializeModules` and exposes them as `ActiveSidebarContent`, `MainEditorContent`, `TerminalContent`.
- Panels are visible or hidden based on `PanelVisibilityMode` and current `WorkspaceMode`. The XAML binds to shell-level content properties, not module types.

**Shell request events** (defined in Core, published by the shell, handled by modules):
```
BuildWorkspaceRequestedEvent
RunProjectRequestedEvent
TestWorkspaceRequestedEvent
RestoreWorkspaceRequestedEvent
CleanWorkspaceRequestedEvent
```
The DotnetCli module subscribes to these in `Initialize` and dispatches to its own internal handlers. No DotnetCli types leak into `MainWindowViewModel`.

---

## 4. SOLID Rules

### 4.1 Single Responsibility
Each class has one reason to change.
- ViewModels manage UI state and delegate to use cases.
- Handlers execute one use case.
- Infrastructure services interact with one external concern.
- Domain types model one concept.
- Modules own one feature area.

### 4.2 Open/Closed
Extend the system by adding new modules, handlers, or services.
Do not modify central switch statements or the Core shell to add features.

### 4.3 Liskov Substitution
Implementations must honor their interface contracts.
Do not throw `NotImplementedException` for supported flows.

### 4.4 Interface Segregation
Keep interfaces small and explicit:
- `IModule`
- `IModuleHost`
- `IWorkspaceContext`
- `ICommandHandler<TCommand>`
- `IQueryHandler<TQuery, TResult>`
- `ITerminalService`
- `ISolutionLoader`

Avoid fat interfaces that mix unrelated responsibilities.

### 4.5 Dependency Inversion
Depend on abstractions defined in Core or Application.
Infrastructure and modules implement those abstractions.
Never depend on concrete infrastructure classes from Application or Domain.

---

## 5. Desktop / Avalonia Rules

### 5.1 MVVM Is Mandatory
- Views (`.axaml`) define layout and bindings only.
- ViewModels expose state, commands, and presentation behavior.
- Business rules and infrastructure calls must not live in code-behind.

### 5.2 Code-Behind Restrictions
Code-behind is allowed only for:
- UI wiring that is hard to express in XAML
- forwarding UI events to ViewModel commands
- view-specific composition with no business logic

Code-behind must not:
- read or write files directly
- spawn processes
- call dotnet CLI directly
- load solutions directly
- contain business decisions

### 5.3 No UI Framework Leakage
Avalonia-specific types must stay in the Desktop project.
Do not use `Window`, `Control`, `Dispatcher`, or `StyledProperty` in Domain, Application, or Core contracts.

### 5.4 Theme and Styling Rules
- Use design tokens for colors, typography, spacing, and sizing.
- Centralize theme files: `Colors.axaml`, `Typography.axaml`, control themes.
- Avoid hardcoded colors and margins in individual views.
- Use Semi.Avalonia as the base and customize on top.

### 5.5 Panel Hosting
The shell must host module panels dynamically.
Panels should appear or hide based on workspace mode and module state.
Do not hardcode panel visibility in the main window layout.

---

## 6. Domain Rules

### 6.1 Keep Domain Pure
Domain must not know about:
- file dialogs
- process execution
- Avalonia controls
- terminal widgets
- LSP/DAP payload DTOs
- module internals

### 6.2 DDD-Light
Use only the complexity that adds value.
Allowed: entities, value objects, aggregates where useful, explicit invariants.
Avoid unless clearly needed: excessive domain events, repositories for everything, over-abstracted factories.

### 6.3 Workspace as Source of Truth
Workspace owns:
- current mode (Empty, FileOnly, Folder, Solution)
- open documents and tabs
- active document
- dirty state
- startup project
- build/debug session state
- module states

Do not duplicate workspace state across ViewModels or modules.

---

## 7. Application Layer Rules

### 7.1 Use Cases First
Application expresses explicit use cases:
- open file / folder / solution
- build / run / test / restore / clean
- save document
- set startup project
- close tab

### 7.2 Commands and Queries
Use internal contracts:
- `ICommandHandler<TCommand>`
- `IQueryHandler<TQuery, TResult>`

Every handler must implement the proper interface so Scrutor can register it.

### 7.3 No Hidden Service Locator
Inject dependencies through constructors.
Do not resolve services ad hoc from `IServiceProvider` except in composition root scenarios.

---

## 8. Infrastructure Rules

### 8.1 Process Execution Boundary
All external process execution goes through:
- `ITerminalService`
- `IProcessHost`

Forbidden outside Infrastructure:
- direct `Process.Start(...)` in Application, Domain, or Desktop
- shell command construction in ViewModels

### 8.2 CLI Wrapping
CLI commands must be explicit and safe.
Always quote paths properly.
Do not construct shell commands unsafely.

### 8.3 Filesystem Access Boundary
Filesystem operations in application workflows go through infrastructure abstractions.
Do not scatter `File.*` and `Directory.*` calls through ViewModels or handlers.

---

## 9. Async / Threading Rules

### 9.1 No Blocking on Async
Forbidden:
- `.Result`
- `.Wait()`
- `.GetAwaiter().GetResult()` in normal application flow

### 9.2 Avoid async void
Use `async Task` except for true UI event handlers.
Commands should use `AsyncRelayCommand` from CommunityToolkit.Mvvm.

### 9.3 UI Thread Discipline
Heavy work must not block the UI thread:
- loading solutions
- running build/test
- reading large files
- module initialization
- protocol communication

---

## 10. State Management Rules

### 10.1 Single Source of Truth
Do not duplicate:
- solution path
- selected project
- active document
- open tab list
- module state

Keep canonical state in Workspace and derive UI state from it.

### 10.2 Explicit Dirty Tracking
Documents must explicitly track modified state.
Tab UI reflects dirty state but does not own the truth.

### 10.3 Workspace Mode Drives UI
The shell must react to workspace mode:
- `Empty` → Welcome Screen
- `FileOnly` → editor-first layout
- `Folder` → File Explorer + editor
- `Solution` → Solution View + editor

---

## 11. Code Style and Maintainability

### 11.1 Clarity Over Cleverness
- Use descriptive names.
- Keep methods small.
- Avoid magic strings and magic numbers.
- Prefer explicit models over anonymous object chains.

### 11.2 No God Classes
Red flags:
- `MainViewModel` doing everything
- `WorkspaceService` becoming a dump of unrelated logic
- giant utility classes

### 11.3 No Generic Junk Drawers
Do not create vague folders or projects:
- `Common`, `Helpers`, `Utils`, `Base`, `Shared`

Name by business or feature concept.

### 11.4 Logging
Log meaningful boundaries:
- module initialized / faulted
- solution opened
- command executed
- process started / finished
- protocol failures
- unhandled exceptions

Do not spam logs with trivial UI noise.

---

## 12. Review Checklist

Use this checklist on every meaningful PR or agent delivery:

- [ ] ViewModels do not contain business logic.
- [ ] Code-behind contains no workflow or infrastructure logic.
- [ ] Domain does not reference Avalonia, Infrastructure, or module internals.
- [ ] Application does not reference Desktop.
- [ ] Modules do not directly reference each other.
- [ ] All cross-module communication goes through Core contracts or Workspace.
- [ ] All process execution is isolated in Infrastructure.
- [ ] All handlers implement the proper internal interfaces.
- [ ] No `.Result` or `.Wait()` in async flows.
- [ ] Workspace state is not duplicated across ViewModels or modules.
- [ ] Theme values are tokenized instead of hardcoded.
- [ ] Module errors are caught at the boundary and reflected in ModuleState.
- [ ] New modules implement IIdeModule and are registered at startup in Bootstrapper.
- [ ] New module ViewModel→View pairs are registered via `IModule.RegisterViews`.
- [ ] New module panels are declared via `Iodule.GetPanelDescriptors`.
- [ ] Modules do not reference Infrastructure directly — they use Core contracts via DI.
- [ ] Raw LSP/DAP DTOs are not leaking through unrelated layers.
- [ ] CLI commands are explicit and safely constructed.
- [ ] Bootstrapper contains no handler or service registrations already owned by a module.
- [ ] `MainWindowViewModel` publishes shell request events via `IShellEventBus` — no DotnetCli command types.
- [ ] Module-specific exceptions inherit from `FluenceException`.
- [ ] Cross-module extension dependencies are acyclic and unidirectional (base module has zero knowledge of extensions).
- [ ] Domain-specific types (command DTOs, domain models, exceptions) live in their module, not Core.

---

## 13. Agent-Specific Directive
The agent must optimize for:
- simplicity
- strict architecture and module boundaries
- reusability
- testability
- minimal desktop complexity
- low UI coupling

The agent must not introduce complexity merely because it is technically possible.
When in doubt, prefer the simpler design that preserves the architecture and module contracts.

---

> ⚠️ **This architecture is subject to future changes.** The `IModule` contract and the three-layer structure (Core / Infrastructure / Desktop + modules) are stable. Details such as `IViewRegistry`, `IPanelDescriptor`, and `IShellEventBus` may evolve as new modules (Git, Lsp, Debug, Agent) are introduced and their requirements become clear.
