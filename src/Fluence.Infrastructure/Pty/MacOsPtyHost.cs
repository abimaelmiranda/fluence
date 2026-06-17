using System;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Models.Infrastructure;

namespace Fluence.Infrastructure.Pty;

public sealed class MacOsPtyHost : IPtyHost
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
        var argv = string.IsNullOrEmpty(arguments)
            ? new[] { executable }
            : new[] { executable, "-c", arguments };

        var env = MacOsPtyInterop.BuildEnvironment();
        var (masterFd, pid) = MacOsPtyInterop.Spawn(executable, argv, workingDirectory, env, columns, rows);
        return new MacOsPtySession(masterFd, pid, columns, rows);
    }

    public IPtySession CreateShellSession(string workingDirectory, int columns = 80, int rows = 24)
    {
        // argv[0] must be the basename so the shell self-identifies correctly (e.g. "zsh", not "/bin/zsh")
        var shellName = System.IO.Path.GetFileName(DefaultShell);
        var argv = new[] { shellName, "-il" };
        var env = MacOsPtyInterop.BuildEnvironment();
        var (masterFd, pid) = MacOsPtyInterop.Spawn(DefaultShell, argv, workingDirectory, env, columns, rows);
        return new MacOsPtySession(masterFd, pid, columns, rows);
    }
}
