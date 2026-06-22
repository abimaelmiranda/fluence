using System.Collections.Generic;

namespace Fluence.Infrastructure;

internal sealed class WindowsPlatformTooling : PlatformTooling
{
    public override string RuntimeId => $"win-{ArchitectureId}";

    public override IReadOnlyList<string> PathExtensions { get; } = [".exe", ".cmd", ".bat", string.Empty];

    public override void MakeExecutable(string executable)
    {
    }
}
