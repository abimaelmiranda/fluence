using System.Collections.Generic;
using System.Diagnostics;

namespace Fluence.Infrastructure;

internal sealed class MacOsPlatformTooling : PlatformTooling
{
    public override string DotnetExecutableName => "dotnet";

    public override string NetcoredbgExecutableName => "netcoredbg";

    public override string OmniSharpExecutableName => "OmniSharp";

    public override string RuntimeId => $"osx-{ArchitectureId}";

    public override IReadOnlyList<string> PathExtensions { get; } = [string.Empty];

    public override string DefaultDotnetRoot => "/usr/local/share/dotnet";

    public override void MakeExecutable(string executable)
    {
        RunProcess("chmod", "+x", executable);
        RunProcess("xattr", "-d", "com.apple.quarantine", executable);
        RunProcess("codesign", "--force", "--sign", "-", executable);
    }

    private static void RunProcess(string file, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        process?.WaitForExit();
    }
}
