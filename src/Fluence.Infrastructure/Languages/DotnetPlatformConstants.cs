using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Fluence.Infrastructure.Languages;

internal static class DotnetPlatformConstants
{
    public static string DotnetExecutableName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";

    public static string NetcoredbgExecutableName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "netcoredbg.exe" : "netcoredbg";

    public static string OmniSharpExecutableName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "OmniSharp.exe" : "OmniSharp";

    public static string DefaultDotnetRoot =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet")
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "/usr/local/share/dotnet"
                : "/usr/share/dotnet";
}
