using System;
using System.IO;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Models.Workspace;

public sealed class OpenDocument
{
    private OpenDocument(
        string path,
        string displayName,
        string content,
        OpenDocumentKind kind,
        object? contentViewModel)
    {
        Path = path;
        DisplayName = displayName;
        Content = content;
        Kind = kind;
        ContentViewModel = contentViewModel;
    }

    public string Path { get; }

    public string DisplayName { get; }

    public OpenDocumentKind Kind { get; }

    public object? ContentViewModel { get; }

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

    public void ReloadContent(string content)
    {
        Content = content;
        IsDirty = false;
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

        return new OpenDocument(path, displayName, content, OpenDocumentKind.TextDocument, null);
    }

    public static OpenDocument FromTool(string id, string displayName, object contentViewModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(contentViewModel);

        return new OpenDocument(id, displayName, string.Empty, OpenDocumentKind.Tool, contentViewModel);
    }
}
