using System.Collections.Generic;

namespace Fluence.Core.Abstractions.Languages;

public interface ILanguageProfileRegistry
{
    void Register(ILanguageProfile profile);

    ILanguageProfile? GetById(string languageId);

    ILanguageProfile? Detect(string workspacePath);

    IReadOnlyList<ILanguageProfile> All { get; }
}
