using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Fluence.Modules.SolutionView;

internal static class SolutionFileTemplateBuilder
{
    public static string Build(string projectPath, string targetDirectory, string fileName, SolutionFileKind kind)
    {
        var typeName = GetTypeName(fileName, kind);
        var @namespace = GetNamespace(projectPath, targetDirectory);

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(@namespace))
        {
            builder.AppendLine($"namespace {@namespace};");
            builder.AppendLine();
        }

        builder.AppendLine(kind switch
        {
            SolutionFileKind.Class => $"public class {typeName}",
            SolutionFileKind.Record => $"public sealed record {typeName}",
            SolutionFileKind.Enum => $"public enum {typeName}",
            SolutionFileKind.Interface => $"public interface {typeName}",
            _ => $"public class {typeName}",
        });
        builder.AppendLine("{");

        if (kind == SolutionFileKind.Enum)
        {
            builder.AppendLine("    None = 0");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GetNamespace(string projectPath, string targetDirectory)
    {
        var rootNamespace = ReadRootNamespace(projectPath);
        if (string.IsNullOrWhiteSpace(rootNamespace))
        {
            rootNamespace = NormalizeNamespace(Path.GetFileNameWithoutExtension(projectPath) ?? string.Empty);
        }
        else
        {
            rootNamespace = NormalizeNamespace(rootNamespace);
        }

        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return rootNamespace ?? string.Empty;
        }

        var relativePath = Path.GetRelativePath(projectDirectory, targetDirectory);
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == "." || relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            return rootNamespace ?? string.Empty;
        }

        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                   .Where(segment => !string.IsNullOrWhiteSpace(segment))
                                   .Select(ToIdentifier)
                                   .Where(segment => !string.IsNullOrWhiteSpace(segment))
                                   .ToArray();

        if (segments.Length == 0)
        {
            return rootNamespace ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(rootNamespace)
            ? string.Join(".", segments)
            : string.Join(".", new[] { rootNamespace }.Concat(segments));
    }

    private static string NormalizeNamespace(string value)
    {
        return string.Join(".",
            value.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                 .Select(ToIdentifier)
                 .Where(segment => !string.IsNullOrWhiteSpace(segment)));
    }

    private static string? ReadRootNamespace(string projectPath)
    {
        try
        {
            var document = XDocument.Load(projectPath);
            return document.Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "RootNamespace", StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string GetTypeName(string fileName, SolutionFileKind kind)
    {
        var typeName = ToIdentifier(Path.GetFileNameWithoutExtension(fileName));
        if (string.IsNullOrWhiteSpace(typeName))
        {
            typeName = "GeneratedType";
        }

        if (kind == SolutionFileKind.Interface && !typeName.StartsWith("I", StringComparison.OrdinalIgnoreCase))
        {
            typeName = $"I{typeName}";
        }

        return typeName;
    }

    private static string ToIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        if (builder.Length > 0 && char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }
}
