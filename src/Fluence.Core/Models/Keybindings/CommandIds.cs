namespace Fluence.Core.Models.Keybindings;

/// <summary>
/// Stable command identifiers used across command registration, keybinding
/// resolution, and native menu wiring. Centralized here so the shell
/// (<c>MainWindowViewModel</c>, <c>NativeMenus</c>) and modules reference a
/// single source of truth instead of repeating string literals.
/// </summary>
public static class CommandIds
{
    // Workbench / shell commands (owned by the desktop shell).
    public const string SaveActiveDocument = "workbench.saveActiveDocument";
    public const string ToggleTerminal = "workbench.toggleTerminal";
    public const string Build = "workbench.build";
    public const string Restore = "workbench.restore";
    public const string Run = "workbench.run";
    public const string Debug = "workbench.debug";
    public const string StopDebug = "workbench.stopDebug";
    public const string Test = "workbench.test";
    public const string Clean = "workbench.clean";
    public const string Publish = "workbench.publish";
    public const string OpenDotnetSdkSetup = "workbench.openDotnetSdkSetup";
    public const string ManageNuGetPackages = "workbench.manageNuGetPackages";
    public const string OpenSettings = "workbench.openSettings";
    public const string OpenKeybindings = "workbench.openKeybindings";
    public const string NextTab = "workbench.nextTab";
    public const string ToggleSidebar = "workbench.toggleSidebar";
    public const string QuickOpenFile = "workbench.quickOpenFile";

    // Debug session commands (owned by the Debug module).
    public const string DebugContinue = "debug.continue";
    public const string DebugStepOver = "debug.stepOver";
    public const string DebugStepInto = "debug.stepInto";
    public const string DebugStepOut = "debug.stepOut";

    // Editor commands (owned by the Editor module).
    public const string EditorTriggerCompletion = "editor.triggerCompletion";
    public const string EditorQuickFix = "editor.quickFix";
    public const string EditorGoToDefinition = "editor.goToDefinition";
    public const string EditorGoToImplementation = "editor.goToImplementation";
    public const string EditorGoToTypeDefinition = "editor.goToTypeDefinition";
}
