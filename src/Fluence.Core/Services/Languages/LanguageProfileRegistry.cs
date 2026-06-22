using System.Collections.Generic;
using System.IO;
using Fluence.Core.Abstractions.Languages;

namespace Fluence.Core.Services.Languages;

public sealed class LanguageProfileRegistry : ILanguageProfileRegistry
{
    private readonly List<ILanguageProfile> _profiles = [];
    private readonly Dictionary<string, ILanguageProfile> _byId = [];
    private readonly Lock _lock = new();

    public IReadOnlyList<ILanguageProfile> All
    {
        get { lock (_lock) return _profiles; }
    }

    public void Register(ILanguageProfile profile)
    {
        lock (_lock)
        {
            _profiles.Add(profile);
            _byId[profile.LanguageId] = profile;
        }
    }

    public ILanguageProfile? GetById(string languageId)
    {
        lock (_lock)
            return _byId.GetValueOrDefault(languageId);
    }

    public ILanguageProfile? Detect(string workspacePath)
    {
        if (!Directory.Exists(workspacePath))
            return null;

        lock (_lock)
        {
            foreach (var profile in _profiles)
                if (profile.MatchesWorkspace(workspacePath))
                    return profile;
            return null;
        }
    }
}
