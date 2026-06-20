using System;
using System.Collections.Generic;
using System.IO;
using Fluence.Core.Abstractions.File;

namespace Fluence.Core.Services.File;

public sealed class FileViewerRegistry : IFileViewerRegistry
{
    private readonly HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string extension) => _extensions.Add(extension);

    public bool HasViewer(string filePath) =>
        _extensions.Contains(Path.GetExtension(filePath));
}
