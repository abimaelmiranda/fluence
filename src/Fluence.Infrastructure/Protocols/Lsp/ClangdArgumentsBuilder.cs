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
        foreach (var candidate in GetCandidateDirectories(rootPath))
        {
            if (File.Exists(Path.Combine(candidate, "compile_commands.json")))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateDirectories(string rootPath)
    {
        yield return rootPath;
        yield return Path.Combine(rootPath, "build");
        yield return Path.Combine(rootPath, "out");
        yield return Path.Combine(rootPath, "out", "build");
        yield return Path.Combine(rootPath, "build", "debug");
        yield return Path.Combine(rootPath, "build", "release");
        yield return Path.Combine(rootPath, "bin", "Debug");
        yield return Path.Combine(rootPath, "bin", "Release");
    }

    private static string QuoteArgument(string value) =>
        '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
}
