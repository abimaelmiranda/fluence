using System;
using System.IO;

namespace Fluence.Core.Workspace;

public sealed class OpenDocument
{
    private OpenDocument(string path, string displayName, string content)
    {
        Path = path;
        DisplayName = displayName;
        Content = content;
    }

    public string Path { get; }

    public string DisplayName { get; }

    public string Content { get; private set; }

    public bool IsDirty { get; private set; }

    public void UpdateContent(string content)
    {
        if (string.Equals(Content, content, StringComparison.Ordinal))
        {
            return;
        }

        Content = content;
        IsDirty = true;
    }

    public void MarkSaved()
    {
        IsDirty = false;
    }

    public static OpenDocument FromPath(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var displayName = System.IO.Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = path;
        }

        return new OpenDocument(path, displayName, content);
    }
}
