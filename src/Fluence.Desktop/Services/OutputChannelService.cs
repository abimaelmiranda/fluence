using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Output;

namespace Fluence.Desktop.Services;

public sealed class OutputChannelService : IOutputChannelService
{
    private const int MaxEntriesPerChannel = 5000;

    private readonly object _gate = new();
    private readonly Dictionary<string, List<OutputChannelEntry>> _entries = new(StringComparer.Ordinal);

    public event EventHandler<OutputChannelChangedEventArgs>? ChannelChanged;

    public IReadOnlyList<OutputChannelEntry> GetEntries(string channelId)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(channelId, out var entries)
                ? entries.ToArray()
                : [];
        }
    }

    public Task WriteAsync(
        string channelId,
        string text,
        OutputChannelEntryKind kind = OutputChannelEntryKind.Information,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || string.IsNullOrEmpty(text))
            return Task.CompletedTask;

        lock (_gate)
        {
            if (!_entries.TryGetValue(channelId, out var entries))
            {
                entries = [];
                _entries[channelId] = entries;
            }

            entries.Add(new OutputChannelEntry(text, kind, DateTimeOffset.Now));
            if (entries.Count > MaxEntriesPerChannel)
                entries.RemoveRange(0, entries.Count - MaxEntriesPerChannel);
        }

        ChannelChanged?.Invoke(this, new OutputChannelChangedEventArgs(channelId));
        return Task.CompletedTask;
    }

    public void Clear(string channelId)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(channelId, out var entries) || entries.Count == 0)
                return;

            entries.Clear();
        }

        ChannelChanged?.Invoke(this, new OutputChannelChangedEventArgs(channelId));
    }
}
