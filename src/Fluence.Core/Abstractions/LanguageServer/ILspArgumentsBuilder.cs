using Fluence.Core.Abstractions.Settings;

namespace Fluence.Core.Abstractions.LanguageServer;

/// <summary>
/// Builds the command-line arguments string for a language server process.
/// Implementations are language-specific (OmniSharp, clangd, etc.).
/// </summary>
public interface ILspArgumentsBuilder
{
    string Build(string rootPath, ILspProvisioningService provisioning, ISettingsService settings);
}
