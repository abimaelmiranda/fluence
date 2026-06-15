using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Infrastructure;

public sealed class RecentProjectsService : IRecentProjectsService
{
    private const int MaxRecents = 10;

    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".fluence",
        "recents.json");

    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<RecentProject>? _cache;

    public IReadOnlyList<RecentProject> GetRecents()
    {
        if (_cache is not null)
            return _cache;

        if (!File.Exists(StorePath))
        {
            _cache = [];
            return _cache;
        }

        try
        {
            using var stream = File.OpenRead(StorePath);
            var data = JsonSerializer.Deserialize(stream, RecentProjectsJsonContext.Default.RecentProjectsData);
            _cache = data?.Recents ?? [];
        }
        catch
        {
            _cache = [];
        }

        return _cache;
    }

    public async Task AddAsync(string path, RecentProjectKind kind, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var recents = await ReadStoreAsync(cancellationToken);

            recents.RemoveAll(r => string.Equals(r.Path, path, StringComparison.Ordinal));

            var name = kind == RecentProjectKind.Solution
                ? Path.GetFileNameWithoutExtension(path)
                : Path.GetFileName(path);

            recents.Insert(0, new RecentProject
            {
                Path = path,
                Name = name ?? path,
                Kind = kind,
                LastOpened = DateTimeOffset.UtcNow,
            });

            if (recents.Count > MaxRecents)
                recents.RemoveRange(MaxRecents, recents.Count - MaxRecents);

            _cache = recents;
            await WriteStoreAsync(new RecentProjectsData { Recents = recents }, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<List<RecentProject>> ReadStoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(StorePath))
            return [];

        try
        {
            await using var stream = File.OpenRead(StorePath);
            var data = await JsonSerializer.DeserializeAsync(
                stream,
                RecentProjectsJsonContext.Default.RecentProjectsData,
                cancellationToken);
            return data?.Recents ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task WriteStoreAsync(RecentProjectsData data, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        await using var stream = File.Create(StorePath);
        await JsonSerializer.SerializeAsync(
            stream,
            data,
            RecentProjectsJsonContext.Default.RecentProjectsData,
            cancellationToken);
    }
}
