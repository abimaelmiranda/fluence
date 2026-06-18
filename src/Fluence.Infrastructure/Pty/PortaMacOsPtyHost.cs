using System;
using System.Collections.Generic;
using Fluence.Core.Abstractions.Infrastructure;
using Porta.Pty;

namespace Fluence.Infrastructure.Pty;

public sealed class PortaMacOsPtyHost : IPtyHost
{
    private static readonly string DefaultShell =
        Environment.GetEnvironmentVariable("SHELL") ?? "/bin/zsh";

    public IPtySession CreateSession(
        string executable,
        string arguments,
        string workingDirectory,
        int columns = 80,
        int rows = 24)
    {
        var args = string.IsNullOrEmpty(arguments) ? Array.Empty<string>() : new[] { arguments };
        return Spawn(executable, args, workingDirectory, columns, rows);
    }

    public IPtySession CreateShellSession(string workingDirectory, int columns = 80, int rows = 24)
        => Spawn(DefaultShell, new[] { "-il" }, workingDirectory, columns, rows);

    private static PortaPtySession Spawn(
        string executable, string[] args, string workingDirectory, int columns, int rows)
    {
        var env = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            if (e.Key is string k && e.Value is string v)
                env[k] = v;
        env["TERM"] = "xterm-256color";

        var options = new PtyOptions
        {
            Name = "xterm-256color",
            Cols = columns,
            Rows = rows,
            Cwd = workingDirectory,
            App = executable,
            CommandLine = args,
            Environment = env,
        };

        // SpawnAsync is awaited synchronously here; this method is always called
        // from Task.Run in TerminalService.CreatePtySessionAsync, so no deadlock risk.
        var conn = PtyProvider.SpawnAsync(options, default).GetAwaiter().GetResult();
        return new PortaPtySession(conn);
    }
}
