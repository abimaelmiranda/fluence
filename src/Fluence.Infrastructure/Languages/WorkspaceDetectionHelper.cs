using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fluence.Infrastructure.Languages;

internal static class WorkspaceDetectionHelper
{
    internal static bool HasAnyFile(string folderPath, IReadOnlyList<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            try
            {
                if (Directory.EnumerateFiles(folderPath, pattern, SearchOption.AllDirectories).Any())
                    return true;
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        return false;
    }
}
