namespace Fluence.Core.Abstractions.LanguageServer;

/// <summary>
/// Extends ILspProvisioningService with .NET-specific provisioning details.
/// Used only by C# language infrastructure; not part of the generic LSP contract.
/// </summary>
public interface IDotnetLspProvisioningService : ILspProvisioningService
{
    string? GetDotnetHostPath();

    string? GetSelectedSdkPath(string? rootPath = null);
}
