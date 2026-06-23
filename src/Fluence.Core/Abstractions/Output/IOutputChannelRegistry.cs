using System;
using System.Collections.Generic;
using Fluence.Core.Models.Output;

namespace Fluence.Core.Abstractions.Output;

public interface IOutputChannelRegistry
{
    IReadOnlyList<OutputChannelDescriptor> Channels { get; }
    event EventHandler? ChannelsChanged;
    void Register(OutputChannelDescriptor descriptor);
}
