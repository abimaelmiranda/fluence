using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Infrastructure;

namespace Fluence.Infrastructure.Protocols.Lsp;

public sealed class ClangdProvisioningService : ILspProvisioningService
{
    private string? _resolvedPath;

    private string? ResolvedPath => _resolvedPath ??= PlatformTooling.Current.FindOnPath("clangd");

    public bool IsProvisioned() => ResolvedPath is not null;

    public string GetExecutablePath() => ResolvedPath ?? "clangd";

    public IReadOnlyDictionary<string, string> GetLaunchEnvironment() => new Dictionary<string, string>();

    public Task ProvisionAsync(Action<string> onOutput, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var executable = ResolvedPath;
        if (executable is null)
            throw new InvalidOperationException("clangd was not found on PATH. Install clangd and retry.");

        onOutput($"[Fluence] clangd found at: {executable}");
        return Task.CompletedTask;
    }
}
