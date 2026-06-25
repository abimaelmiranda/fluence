namespace Fluence.Core.Models.Toolchains;

public enum ToolchainCommandKind
{
    BuildWorkspace,
    BuildProject,
    RunProject,
    RunSpecificProject,
    TestWorkspace,
    TestProject,
    RestoreWorkspace,
    RestoreProject,
    CleanWorkspace,
    CleanProject,
    DebugProject,
    StartLanguageServer,
}
