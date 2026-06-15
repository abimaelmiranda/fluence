using System.Collections.Concurrent;
using Avalonia.Media.Imaging;

namespace Fluence.Modules.NuGetExplorer.Services;

public sealed class PackageIconLoader
{
    private static readonly HttpClient Http = new();
    private readonly ConcurrentDictionary<string, Bitmap> _cache = new(StringComparer.Ordinal);

    public async Task<Bitmap?> LoadAsync(string? iconUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(iconUrl) || !Uri.TryCreate(iconUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (_cache.TryGetValue(iconUrl, out var cached))
        {
            return cached;
        }

        await using var stream = await Http.GetStreamAsync(uri, cancellationToken);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        var bitmap = new Bitmap(memory);
        _cache[iconUrl] = bitmap;
        return bitmap;
    }
}
