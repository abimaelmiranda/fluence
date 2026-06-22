using System.IO;
using Avalonia.Media;

namespace Fluence.Modules.SolutionView.Views.Converters;

internal static class SolutionTreeIconCatalog
{
    public static readonly StreamGeometry SolutionIcon = StreamGeometry.Parse("M2 4 L8 1.5 L14 4 L14 12 L8 14.5 L2 12 Z M5 5.2 L11 5.2 L11 10.8 L5 10.8 Z");
    public static readonly StreamGeometry FolderIcon = StreamGeometry.Parse("M1 4 L6.2 4 L7.4 5.2 L15 5.2 L15 13 L1 13 Z");
    public static readonly StreamGeometry ProjectIcon = StreamGeometry.Parse("M2 4 L8 1.5 L14 4 L14 12 L8 14.5 L2 12 Z M5 5.3 L11 5.3 L11 6.5 L5 6.5 Z M5 7.5 L11 7.5 L11 8.7 L5 8.7 Z M5 9.7 L9 9.7 L9 10.9 L5 10.9 Z");
    public static readonly StreamGeometry DependenciesIcon = StreamGeometry.Parse("M3 3 L6 3 L6 6 L3 6 Z M10 3 L13 3 L13 6 L10 6 Z M6.5 10 L9.5 10 L9.5 13 L6.5 13 Z M4.5 6 L7.5 10 L8.5 10 L11.5 6 Z");
    public static readonly StreamGeometry ReferenceIcon = StreamGeometry.Parse("M4.4 5 A3 3 0 0 1 7.4 2 L9.2 2 L9.2 4 L7.4 4 A1 1 0 0 0 7.4 6 L9.2 6 L9.2 8 L7.4 8 A3 3 0 0 1 4.4 5 M6.8 8 L8.6 8 A1 1 0 0 1 8.6 10 L6.8 10 L6.8 12 L8.6 12 A3 3 0 0 0 8.6 6 L6.8 6 Z");
    public static readonly StreamGeometry PackageIcon = StreamGeometry.Parse("M2 5 L8 2 L14 5 L8 8 Z M2 5 L2 11 L8 14 L8 8 Z M14 5 L14 11 L8 14 L8 8 Z");
    public static readonly StreamGeometry FileIcon = StreamGeometry.Parse("M3 1.5 L9.8 1.5 L13 4.7 L13 14.5 L3 14.5 Z M9.8 1.5 L9.8 4.7 L13 4.7 Z");

    public static readonly IBrush SolutionBrush = Brush("#6FC3FF");
    public static readonly IBrush FolderBrush = Brush("#D6A83D");
    public static readonly IBrush ProjectBrush = Brush("#4FA3FF");
    public static readonly IBrush DependencyBrush = Brush("#8B98A7");
    public static readonly IBrush ReferenceBrush = Brush("#7BCB8F");
    public static readonly IBrush PackageBrush = Brush("#B896FF");
    public static readonly IBrush CSharpBrush = Brush("#A877FF");
    public static readonly IBrush CBrush = Brush("#57C7A6");
    public static readonly IBrush CppBrush = Brush("#4FA3FF");
    public static readonly IBrush HeaderBrush = Brush("#7BCB8F");
    public static readonly IBrush MarkupBrush = Brush("#F28B54");
    public static readonly IBrush JsonBrush = Brush("#E2C044");
    public static readonly IBrush MarkdownBrush = Brush("#86A8FF");
    public static readonly IBrush BuildBrush = Brush("#9AB1C7");
    public static readonly IBrush FileBrush = Brush("#AAB4BF");

    public static string GetTextIcon(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Makefile", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("gnumakefile", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("compile_commands.json", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".props" or ".targets" or ".axaml" or ".xaml" or ".xml" => "<>",
            ".json" => "{}",
            ".md" => "MD",
            _ => string.Empty,
        };
    }

    public static IBrush GetFileBrush(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Makefile", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("gnumakefile", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("compile_commands.json", StringComparison.OrdinalIgnoreCase))
        {
            return BuildBrush;
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => CSharpBrush,
            ".c" => CBrush,
            ".cc" or ".cpp" or ".cxx" => CppBrush,
            ".h" or ".hh" or ".hpp" or ".hxx" => HeaderBrush,
            ".csproj" or ".props" or ".targets" => ProjectBrush,
            ".sln" or ".slnx" => SolutionBrush,
            ".axaml" or ".xaml" or ".xml" => MarkupBrush,
            ".json" => JsonBrush,
            ".md" => MarkdownBrush,
            _ => FileBrush,
        };
    }

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
}
