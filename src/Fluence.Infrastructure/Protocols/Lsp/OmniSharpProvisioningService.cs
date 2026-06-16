using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Storage;

namespace Fluence.Infrastructure.Protocols.Lsp;

public sealed partial class OmniSharpProvisioningService(IFluenceStorageService storage) : ILspProvisioningService
{
    private const string OmniSharpApiUrl = "https://api.github.com/repos/OmniSharp/omnisharp-roslyn/releases/latest";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private string InstallDir => storage.GetUserPath("LanguageServer");

    static OmniSharpProvisioningService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("FluenceIDE");
    }

    public bool IsProvisioned() => File.Exists(GetExecutablePath());

    public string GetExecutablePath() => Path.Combine(InstallDir, ResolveExecutableName());

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
        await EnsureInstallDirectoryAsync(InstallDir, onOutput, cancellationToken).ConfigureAwait(false);
        await DownloadBinaryAsync(onOutput, cancellationToken).ConfigureAwait(false);
    }
}
