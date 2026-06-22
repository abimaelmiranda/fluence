using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Infrastructure;

namespace Fluence.Infrastructure.Protocols.Lsp;

public sealed class ClangdProvisioningService : ILspProvisioningService
{
    public bool IsProvisioned() => PlatformTooling.Current.FindOnPath("clangd") is not null;

    public string GetExecutablePath() =>
        PlatformTooling.Current.FindOnPath("clangd")
        ?? "clangd";

    public IReadOnlyDictionary<string, string> GetLaunchEnvironment() => new Dictionary<string, string>();

    public Task ProvisionAsync(Action<string> onOutput, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var executable = PlatformTooling.Current.FindOnPath("clangd");
        if (executable is null)
            throw new InvalidOperationException("clangd was not found on PATH. Install clangd and retry.");

        onOutput($"[Fluence] clangd found at: {executable}");
        return Task.CompletedTask;
    }
}
