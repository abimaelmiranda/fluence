using System;
using System.Collections.Generic;
using System.Threading;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Models.Output;

namespace Fluence.Desktop.Services;

internal sealed class OutputChannelRegistry : IOutputChannelRegistry
{
    private readonly Lock _lock = new();
    private readonly List<OutputChannelDescriptor> _channels = [];

    public IReadOnlyList<OutputChannelDescriptor> Channels
    {
        get
        {
            lock (_lock)
                return [.. _channels];
        }
    }

    public event EventHandler? ChannelsChanged;

    public void Register(OutputChannelDescriptor descriptor)
    {
        bool added;
        lock (_lock)
        {
            if (_channels.Exists(c => string.Equals(c.Id, descriptor.Id, StringComparison.Ordinal)))
                return;
            _channels.Add(descriptor);
            added = true;
        }

        if (added)
            ChannelsChanged?.Invoke(this, EventArgs.Empty);
    }
}
