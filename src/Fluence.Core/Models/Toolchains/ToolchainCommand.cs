namespace Fluence.Core.Models.Toolchains;

public sealed record ToolchainCommand(ToolchainCommandKind Kind, string? ProjectPath = null);
