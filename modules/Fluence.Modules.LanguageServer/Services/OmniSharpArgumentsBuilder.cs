using System;
using System.Collections.Generic;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Settings;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed class OmniSharpArgumentsBuilder : ILspArgumentsBuilder
{
    public string Build(string rootPath, ILspProvisioningService provisioning, ISettingsService settings)
    {
        var s = settings.Get<LanguageServerSettings>();
        var sdkPath = (provisioning as IDotnetLspProvisioningService)?.GetSelectedSdkPath(rootPath);

        var arguments = new List<string>
        {
            "--languageserver",
            "-z",
            "-s",
            QuoteArgument(rootPath),
            $"--msbuild:enabled={Bool(s.EnableMsBuild)}",
            $"--msbuild:loadProjectsOnDemand={Bool(s.LoadProjectsOnDemand)}",
            $"--msbuild:EnablePackageAutoRestore={Bool(s.EnablePackageAutoRestore)}",
            $"--RoslynExtensionsOptions:enableAnalyzersSupport={Bool(s.EnableAnalyzersSupport)}",
            $"--RoslynExtensionsOptions:enableDecompilationSupport={Bool(s.EnableDecompilationSupport)}",
            $"--RoslynExtensionsOptions:enableImportCompletion={Bool(s.EnableImportCompletion)}",
            $"--RoslynExtensionsOptions:diagnosticWorkersThreadCount={Math.Max(1, s.DiagnosticWorkersThreadCount)}",
            $"--FormattingOptions:enableEditorConfigSupport={Bool(s.EnableEditorConfigSupport)}",
            $"--sdk:includePrereleases={Bool(s.IncludePrereleases)}",
        };

        if (!string.IsNullOrWhiteSpace(sdkPath))
            arguments.Add($"--sdk:path={QuoteArgument(sdkPath)}");

        return string.Join(' ', arguments);
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string QuoteArgument(string value) =>
        '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
}
