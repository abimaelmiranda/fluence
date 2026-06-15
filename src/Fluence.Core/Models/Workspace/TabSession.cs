using System;
using System.Collections.Generic;
using System.Linq;

namespace Fluence.Core.Models.Workspace;

public sealed class TabSession
{
    private TabSession(IReadOnlyList<OpenDocument> documents, OpenDocument? activeDocument)
    {
        Documents = documents;
        ActiveDocument = activeDocument;
    }

    public static TabSession Empty { get; } = new(Array.Empty<OpenDocument>(), null);

    public IReadOnlyList<OpenDocument> Documents { get; }

    public OpenDocument? ActiveDocument { get; }

    public static TabSession FromActiveDocument(OpenDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new TabSession([document], document);
    }

    public TabSession AddOrActivate(OpenDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var existingDocument = Documents.FirstOrDefault(item => string.Equals(item.Path, document.Path, StringComparison.Ordinal));
        if (existingDocument is not null)
        {
            return new TabSession(Documents, existingDocument);
        }

        var documents = Documents.Concat([document]).ToArray();
        return new TabSession(documents, document);
    }

    public TabSession Activate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var document = Documents.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.Ordinal));
        return document is null ? this : new TabSession(Documents, document);
    }

    public TabSession Close(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var documents = Documents
            .Where(item => !string.Equals(item.Path, path, StringComparison.Ordinal))
            .ToArray();

        if (documents.Length == 0)
        {
            return Empty;
        }

        var activeDocument = ActiveDocument is not null
            && documents.Any(item => string.Equals(item.Path, ActiveDocument.Path, StringComparison.Ordinal))
                ? ActiveDocument
                : documents[^1];

        return new TabSession(documents, activeDocument);
    }
}
