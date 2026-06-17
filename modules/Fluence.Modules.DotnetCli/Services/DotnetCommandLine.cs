using System;

namespace Fluence.Modules.DotnetCli.Services;

internal static class DotnetCommandLine
{
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
