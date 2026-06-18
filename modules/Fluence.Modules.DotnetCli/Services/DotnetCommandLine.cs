using System;
using System.Collections.Generic;
using System.Linq;

namespace Fluence.Modules.DotnetCli.Services;

internal static class DotnetCommandLine
{
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    public static string Format(string executable, IReadOnlyList<string> arguments)
    {
        return Quote(executable) + " " + string.Join(" ", arguments.Select(Quote));
    }
}
