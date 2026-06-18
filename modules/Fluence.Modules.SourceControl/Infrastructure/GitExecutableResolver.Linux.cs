using System.IO;

namespace Fluence.Modules.SourceControl.Infrastructure;

internal sealed class LinuxGitExecutableResolver : GitExecutableResolver
{
    public override string Resolve() =>
        FindOnPath("git", [string.Empty]) ??
        (File.Exists("/usr/bin/git") ? "/usr/bin/git" : "git");
}
