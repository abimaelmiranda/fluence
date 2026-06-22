using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Svg.Skia;

namespace Fluence.Shared.MaterialIcons;

internal sealed class MaterialIconTheme
{
    private const string ManifestRelativePath = "MaterialIcons/dist/material-icons.json";
    private const string MaterialIconsRoot = "MaterialIcons";

    private static readonly Lazy<MaterialIconTheme> LazyInstance = new(() => new MaterialIconTheme());

    private readonly ConcurrentDictionary<string, Bitmap?> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _iconPathById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _fileIconIdByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _fileIconIdByExtension = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _folderIconIdByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _rootDirectory;
    private string? _defaultFileIconPath;
    private string? _defaultFolderIconPath;

    private MaterialIconTheme()
    {
        _rootDirectory = Path.Combine(AppContext.BaseDirectory, MaterialIconsRoot);
        LoadManifest();
    }

    public static MaterialIconTheme Instance => LazyInstance.Value;

    public Bitmap? GetFileIcon(string path)
    {
        var fileName = Path.GetFileName(path);
        var iconId = ResolveFileIconId(fileName);
        return LoadIcon(iconId is null ? _defaultFileIconPath : ResolveIconPath(iconId) ?? _defaultFileIconPath);
    }

    public Bitmap? GetFolderIcon(string folderName)
    {
        var iconId = _folderIconIdByName.TryGetValue(folderName, out var folderIconId)
            ? folderIconId
            : null;

        return LoadIcon(iconId is null ? _defaultFolderIconPath : ResolveIconPath(iconId) ?? _defaultFolderIconPath);
    }

    public Bitmap? GetIconById(string iconId)
        => LoadIcon(ResolveIconPath(iconId) ?? _defaultFileIconPath);

    private string? ResolveFileIconId(string fileName)
    {
        if (_fileIconIdByName.TryGetValue(fileName, out var fileNameIconId))
            return fileNameIconId;

        foreach (var candidate in GetExtensionCandidates(fileName))
        {
            if (_fileIconIdByExtension.TryGetValue(candidate, out var extensionIconId))
                return extensionIconId;
        }

        return null;
    }

    private IEnumerable<string> GetExtensionCandidates(string fileName)
    {
        var normalized = fileName.Trim().ToLower(CultureInfo.InvariantCulture);
        for (var index = normalized.IndexOf('.'); index >= 0 && index < normalized.Length - 1; index = normalized.IndexOf('.', index + 1))
            yield return normalized[(index + 1)..];
    }

    private Bitmap? LoadIcon(string? relativeIconPath)
    {
        if (string.IsNullOrWhiteSpace(relativeIconPath))
            return null;

        return _imageCache.GetOrAdd(relativeIconPath, LoadIconCore);
    }

    private Bitmap? LoadIconCore(string relativeIconPath)
    {
        try
        {
            foreach (var candidate in EnumerateCandidatePaths(relativeIconPath))
            {
                if (!File.Exists(candidate))
                    continue;

                if (Path.GetExtension(candidate).Equals(".svg", StringComparison.OrdinalIgnoreCase))
                    return LoadSvgBitmap(candidate);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveIconPath(string iconId)
        => _iconPathById.TryGetValue(iconId, out var iconPath)
            ? NormalizeIconPath(iconPath)
            : null;

    private IEnumerable<string> EnumerateCandidatePaths(string relativeIconPath)
    {
        var normalized = NormalizeIconPath(relativeIconPath);
        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        var svgPath = Path.GetFullPath(Path.Combine(_rootDirectory, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (svgPath.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase))
            yield return svgPath;
    }

    private static Bitmap? LoadSvgBitmap(string svgPath)
    {
        using var svg = SKSvg.CreateFromFile(svgPath);
        var picture = svg.Picture ?? svg.Load(svgPath);
        if (picture is null)
            return null;

        var bounds = picture.CullRect;
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height));

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded is null)
            return null;

        using var stream = encoded.AsStream();
        return new Bitmap(stream);
    }

    private void LoadManifest()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, ManifestRelativePath);
        if (!File.Exists(manifestPath))
            return;

        using var stream = File.OpenRead(manifestPath);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        LoadIconDefinitions(root);
        LoadStringMap(root, "fileNames", _fileIconIdByName);
        LoadStringMap(root, "fileExtensions", _fileIconIdByExtension);
        LoadStringMap(root, "folderNames", _folderIconIdByName);

        _defaultFileIconPath = ReadIconPath(root, "file");
        _defaultFolderIconPath = ReadIconPath(root, "folder");
    }

    private void LoadIconDefinitions(JsonElement root)
    {
        if (!root.TryGetProperty("iconDefinitions", out var definitions) || definitions.ValueKind != JsonValueKind.Object)
            return;

        foreach (var definition in definitions.EnumerateObject())
        {
            if (definition.Value.TryGetProperty("iconPath", out var iconPath) && iconPath.ValueKind == JsonValueKind.String)
                _iconPathById[definition.Name] = NormalizeIconPath(iconPath.GetString()) ?? string.Empty;
        }
    }

    private static void LoadStringMap(JsonElement root, string propertyName, Dictionary<string, string> target)
    {
        if (!root.TryGetProperty(propertyName, out var source) || source.ValueKind != JsonValueKind.Object)
            return;

        foreach (var item in source.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.String)
                target[item.Name] = item.Value.GetString() ?? string.Empty;
        }
    }

    private string? ReadIconPath(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var definition))
            return null;

        if (definition.ValueKind == JsonValueKind.String)
            return ResolveIconPath(definition.GetString() ?? string.Empty);

        if (definition.ValueKind != JsonValueKind.Object ||
            !definition.TryGetProperty("iconPath", out var iconPath) ||
            iconPath.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return ResolveIconPath(iconPath.GetString());
    }

    private static string? NormalizeIconPath(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
            return null;

        var normalized = iconPath.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        while (normalized.StartsWith("../", StringComparison.Ordinal))
            normalized = normalized[3..];

        return normalized;
    }

}
