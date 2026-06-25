using Fluence.Core.Abstractions.Languages;
using Fluence.Core.Abstractions.Toolchains;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Toolchains;

namespace Fluence.Core.Services.Toolchains;

public sealed class ToolchainRegistry(
    IWorkspaceContext workspace,
    ILanguageProfileRegistry profiles) : IToolchainRegistry
{
    private readonly Dictionary<string, IToolchain> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IToolchain> _byLanguage = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public IToolchain Active
    {
        get
        {
            var languageId = profiles.DetectWorkspaceLanguage(workspace) ?? "csharp";
            lock (_lock)
            {
                if (_byLanguage.TryGetValue(languageId, out var toolchain))
                    return toolchain;
                if (_byLanguage.TryGetValue("csharp", out toolchain))
                    return toolchain;
            }

            throw new InvalidOperationException($"No toolchain registered for '{languageId}'.");
        }
    }

    public void Register(IToolchain toolchain)
    {
        lock (_lock)
        {
            _byId[toolchain.Id] = toolchain;
            _byLanguage[toolchain.LanguageId] = toolchain;
        }
    }

    public IToolchain? GetById(string id)
    {
        lock (_lock)
            return _byId.GetValueOrDefault(id);
    }

    public Task ExecuteAsync(ToolchainCommand command, CancellationToken cancellationToken = default) =>
        Active.ExecuteAsync(command, cancellationToken);
}
