using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;

namespace Fluence.Infrastructure.Protocols.Lsp;

public sealed partial class OmniSharpProvisioningService : ILspProvisioningService
{
    private const string OmniSharpApiUrl = "https://api.github.com/repos/OmniSharp/omnisharp-roslyn/releases/latest";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    static OmniSharpProvisioningService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("FluenceIDE");
    }

    public bool IsProvisioned() => File.Exists(GetExecutablePath());

    public string GetExecutablePath()
    {
        var dir = ResolveGlobalInstallDir();
        return Path.Combine(dir, ResolveExecutableName());
    }

    public IReadOnlyDictionary<string, string> GetLaunchEnvironment()
    {
        var dotnetRoot = ResolveDotnetDir();
        return new Dictionary<string, string>
        {
            ["DOTNET_ROOT"] = dotnetRoot,
        };
    }

    public async Task ProvisionAsync(Action<string> onOutput, CancellationToken cancellationToken = default)
    {
        await EnsureInstallDirectoryAsync(onOutput, cancellationToken).ConfigureAwait(false);
        await DownloadBinaryAsync(onOutput, cancellationToken).ConfigureAwait(false);
    }
}
