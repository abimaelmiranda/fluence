using System;
using System.IO;
using System.Xml.Linq;

namespace Fluence.Modules.DotnetCli.Services;

internal static class DotnetProjectRunCapability
{
    public static bool CanRun(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath) || !DotnetPathHelpers.IsProjectPath(projectPath))
            return false;

        try
        {
            var document = XDocument.Load(projectPath);
            var sdk = document.Root?.Attribute("Sdk")?.Value ?? string.Empty;
            if (sdk.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
                return true;

            var hasExplicitLibraryOutputType = false;
            foreach (var element in document.Descendants())
            {
                if (!string.Equals(element.Name.LocalName, "OutputType", StringComparison.OrdinalIgnoreCase))
                    continue;

                var outputType = element.Value.Trim();
                if (string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(outputType, "Library", StringComparison.OrdinalIgnoreCase))
                    hasExplicitLibraryOutputType = true;
            }

            return !hasExplicitLibraryOutputType && HasProgramFile(projectPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasProgramFile(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        return !string.IsNullOrWhiteSpace(projectDirectory) &&
               File.Exists(Path.Combine(projectDirectory, "Program.cs"));
    }
}
