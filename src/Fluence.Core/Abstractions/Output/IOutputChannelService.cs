using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Abstractions.Output;

public interface IOutputChannelService
{
    event EventHandler<OutputChannelChangedEventArgs>? ChannelChanged;

    IReadOnlyList<OutputChannelEntry> GetEntries(string channelId);

    Task WriteAsync(
        string channelId,
        string text,
        OutputChannelEntryKind kind = OutputChannelEntryKind.Information,
        CancellationToken cancellationToken = default);

    void Clear(string channelId);
}

public sealed class OutputChannelChangedEventArgs(string channelId) : EventArgs
{
    public string ChannelId { get; } = channelId;
}

public sealed record OutputChannelEntry(
    string Text,
    OutputChannelEntryKind Kind,
    DateTimeOffset Timestamp);

public enum OutputChannelEntryKind
{
    Information,
    Warning,
    Error,
}
