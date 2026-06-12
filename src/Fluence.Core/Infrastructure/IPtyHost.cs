namespace Fluence.Core.Infrastructure;

public interface IPtyHost
{
    IPtySession CreateSession(
        string executable,
        string arguments,
        string workingDirectory,
        int columns = 80,
        int rows = 24);

    IPtySession CreateShellSession(
        string workingDirectory,
        int columns = 80,
        int rows = 24);
}
