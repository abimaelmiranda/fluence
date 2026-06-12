using System;
using Fluence.Core.Infrastructure;

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
        // Start an interactive shell. Login shells may run profile scripts that block IDE startup.
        var argv = new[] { DefaultShell, "-i" };
        var env = MacOsPtyInterop.BuildEnvironment();
        var (masterFd, pid) = MacOsPtyInterop.Spawn(DefaultShell, argv, workingDirectory, env, columns, rows);
        return new MacOsPtySession(masterFd, pid, columns, rows);
    }
}
