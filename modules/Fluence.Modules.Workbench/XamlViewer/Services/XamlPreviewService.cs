using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Fluence.Modules.Workbench.XamlViewer.Abstractions;

namespace Fluence.Modules.Workbench.XamlViewer.Services;

internal sealed class XamlPreviewService : IXamlPreviewService
{
    private const string WpfNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly Regex StripXClassRegex = new(@"\s*x:Class=""[^""]*""", RegexOptions.Compiled);

    public Control? Render(string xamlContent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsWpfXaml(xamlContent))
            throw new NotSupportedException("WPF XAML is not supported. Only Avalonia XAML files (.axaml) can be previewed.");

        var cleaned = StripXClassRegex.Replace(xamlContent, string.Empty);

        var doc = new RuntimeXamlLoaderDocument(cleaned);
        var config = new RuntimeXamlLoaderConfiguration
        {
            DesignMode = true,
            UseCompiledBindingsByDefault = false,
        };

        return AvaloniaRuntimeXamlLoader.Load(doc, config) as Control;
    }

    private static bool IsWpfXaml(string xamlContent)
    {
        try
        {
            using var reader = XmlReader.Create(
                new StringReader(xamlContent),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                    return reader.LookupNamespace(string.Empty) == WpfNamespace;
            }
        }
        catch { }
        return false;
    }
}
