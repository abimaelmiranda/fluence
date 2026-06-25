namespace Fluence.Core.Models.Toolchains;

[Flags]
public enum ToolchainCapability
{
    None = 0,
    Sdk = 1 << 0,
    Build = 1 << 1,
    Run = 1 << 2,
    Test = 1 << 3,
    Restore = 1 << 4,
    Clean = 1 << 5,
    LanguageServer = 1 << 6,
    Debugger = 1 << 7,
}
