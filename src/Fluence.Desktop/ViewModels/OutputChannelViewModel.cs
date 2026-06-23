using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Models.Output;
using Fluence.Core.ViewModels;

namespace Fluence.Desktop.ViewModels;

public sealed partial class OutputChannelViewModel : ViewModelBase, IDisposable
{
    private readonly IOutputChannelService _channels;
    private readonly IOutputChannelRegistry _registry;
    private readonly bool _locked;
    private OutputChannelDescriptor? _selectedChannel;
    private OutputLogLevel _selectedLogLevel = OutputLogLevel.All;
    private string _text = string.Empty;

    /// <summary>
    /// Multi-channel mode: user can switch channels via combobox.
    /// </summary>
    public OutputChannelViewModel(IOutputChannelService channels, IOutputChannelRegistry registry)
        : this(channels, registry, null) { }

    /// <summary>
    /// Locked mode: fixed to a specific channel, combobox hidden.
    /// Used by Debug and Run tabs whose UI is not changed in this milestone.
    /// </summary>
    public OutputChannelViewModel(IOutputChannelService channels, IOutputChannelRegistry registry, string? lockedChannelId)
    {
        _channels = channels;
        _registry = registry;
        _locked = lockedChannelId is not null;

        foreach (var ch in registry.Channels)
            AvailableChannels.Add(ch);

        _selectedChannel = _locked
            ? AvailableChannels.FirstOrDefault(c => string.Equals(c.Id, lockedChannelId, StringComparison.Ordinal))
              ?? new OutputChannelDescriptor(lockedChannelId!, lockedChannelId!)
            : AvailableChannels.FirstOrDefault();

        _registry.ChannelsChanged += OnChannelsChanged;
        _channels.ChannelChanged += OnChannelChanged;

        Refresh();
    }

    public static IReadOnlyList<OutputLogLevel> AvailableLogLevels { get; } = Enum.GetValues<OutputLogLevel>();

    public ObservableCollection<OutputChannelDescriptor> AvailableChannels { get; } = [];

    public bool IsChannelSelectorVisible => !_locked;

    public OutputChannelDescriptor? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (_locked) return;
            if (SetProperty(ref _selectedChannel, value))
                Refresh();
        }
    }

    public OutputLogLevel SelectedLogLevel
    {
        get => _selectedLogLevel;
        set
        {
            if (SetProperty(ref _selectedLogLevel, value))
                Refresh();
        }
    }

    public string Text
    {
        get => _text;
        private set
        {
            if (SetProperty(ref _text, value))
                OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsEmpty => string.IsNullOrEmpty(Text);

    [RelayCommand]
    private void Clear()
    {
        if (_selectedChannel is not null)
            _channels.Clear(_selectedChannel.Id);
    }

    private void OnChannelsChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(SyncChannels);
    }

    private void SyncChannels()
    {
        foreach (var ch in _registry.Channels)
        {
            if (!AvailableChannels.Any(c => string.Equals(c.Id, ch.Id, StringComparison.Ordinal)))
                AvailableChannels.Add(ch);
        }

        if (!_locked && _selectedChannel is null)
        {
            _selectedChannel = AvailableChannels.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedChannel));
        }

        Refresh();
    }

    private void OnChannelChanged(object? sender, OutputChannelChangedEventArgs e)
    {
        if (_selectedChannel is null ||
            !string.Equals(e.ChannelId, _selectedChannel.Id, StringComparison.Ordinal))
            return;

        Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        if (_selectedChannel is null)
        {
            Text = string.Empty;
            return;
        }

        var entries = _channels.GetEntries(_selectedChannel.Id);
        Text = string.Concat(entries
            .Where(e => e.Kind >= _selectedLogLevel)
            .Select(e => e.Text));
    }

    public void Dispose()
    {
        _channels.ChannelChanged -= OnChannelChanged;
        _registry.ChannelsChanged -= OnChannelsChanged;
    }
}
