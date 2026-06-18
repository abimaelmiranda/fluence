namespace Fluence.Core.Models.Workspace;

/// <summary>
/// Stable tool-tab URIs used to open tool panels in the workbench.
/// Centralized so the shell and modules reference a single source of truth
/// instead of repeating <c>"tool://fluence/..."</c> literals.
/// </summary>
public static class ToolTabIds
{
    public const string Settings = "tool://fluence/settings";
    public const string SettingsTitle = "Settings";

    public const string LspSetup = "tool://fluence/lsp-setup";
    public const string LspSetupTitle = "Language Server Setup";

    public const string DebuggerSetup = "tool://fluence/debugger-setup";
    public const string DebuggerSetupTitle = "Debugger Setup";

    public const string NewProject = "tool://fluence/new-project";
    public const string NewProjectTitle = "New Project";

    public const string Publish = "tool://fluence/publish";
    public const string PublishTitle = "Publish";

    public const string DotnetSdkSetup = "tool://fluence/dotnet-sdk";
    public const string DotnetSdkSetupTitle = ".NET SDK";
}
