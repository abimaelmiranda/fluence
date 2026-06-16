using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Storage;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Infrastructure;

public sealed class RecentProjectsService(IFluenceStorageService storage) : IRecentProjectsService
{
    private const int MaxRecents = 10;
    private const string FileName = "recents.json";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<RecentProject>? _cache;

    public IReadOnlyList<RecentProject> GetRecents()
    {
        if (_cache is not null)
            return _cache;

        var path = storage.GetUserPath(FileName);
        if (!File.Exists(path))
        {
            _cache = [];
            return _cache;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var data = System.Text.Json.JsonSerializer.Deserialize(stream, RecentProjectsJsonContext.Default.RecentProjectsData);
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
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = await storage.ReadUserAsync(FileName, RecentProjectsJsonContext.Default.RecentProjectsData, cancellationToken).ConfigureAwait(false);
            var recents = data?.Recents ?? [];

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
            await storage.WriteUserAsync(FileName, new RecentProjectsData { Recents = recents }, RecentProjectsJsonContext.Default.RecentProjectsData, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
}
