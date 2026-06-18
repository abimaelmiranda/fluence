using System;
using System.Collections.Generic;
using System.IO;

namespace Fluence.Infrastructure;

internal sealed class WindowsPlatformTooling : PlatformTooling
{
    public override string DotnetExecutableName => "dotnet.exe";

    public override string NetcoredbgExecutableName => "netcoredbg.exe";

    public override string OmniSharpExecutableName => "OmniSharp.exe";

    public override string RuntimeId => $"win-{ArchitectureId}";

    public override IReadOnlyList<string> PathExtensions { get; } = [".exe", ".cmd", ".bat", string.Empty];

    public override string DefaultDotnetRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "dotnet");

    public override void MakeExecutable(string executable)
    {
    }
}
