using System;
using System.Collections.Generic;
using System.IO;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Settings;

namespace Fluence.Infrastructure.Protocols.Lsp;

public sealed class ClangdArgumentsBuilder : ILspArgumentsBuilder
{
    public string Build(string rootPath, ILspProvisioningService provisioning, ISettingsService settings)
    {
        _ = provisioning;
        _ = settings;

        var arguments = new List<string>
        {
            "--background-index",
            "--clang-tidy",
            "--completion-style=detailed",
            "--header-insertion=iwyu",
            "--pch-storage=memory",
        };

        var compileCommandsDir = FindCompileCommandsDirectory(rootPath);
        if (compileCommandsDir is not null)
            arguments.Add($"--compile-commands-dir={QuoteArgument(compileCommandsDir)}");

        return string.Join(' ', arguments);
    }

    private static string? FindCompileCommandsDirectory(string rootPath)
    {
        if (File.Exists(Path.Combine(rootPath, "compile_commands.json")))
            return rootPath;

        return null;
    }

    private static string QuoteArgument(string value) =>
        '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
}
