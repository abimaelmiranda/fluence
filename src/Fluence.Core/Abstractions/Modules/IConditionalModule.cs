using Fluence.Core.Abstractions.Languages;
using Fluence.Core.Abstractions.Workspace;

namespace Fluence.Core.Abstractions.Modules;

/// <summary>
/// Optional interface for modules that should only activate for specific workspace languages.
/// Modules that do not implement this interface are always activated.
/// </summary>
public interface IConditionalModule
{
    bool ShouldActivate(IWorkspaceContext workspace, ILanguageProfileRegistry profiles);
}
