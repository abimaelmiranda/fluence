using System;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.ViewModels;

namespace Fluence.Desktop.ViewModels;

public sealed partial class OutputChannelViewModel : ViewModelBase, IDisposable
{
    private readonly IOutputChannelService _channels;
    private readonly string _channelId;
    private string _text = string.Empty;

    public OutputChannelViewModel(IOutputChannelService channels, string channelId)
    {
        _channels = channels;
        _channelId = channelId;
        _channels.ChannelChanged += OnChannelChanged;
        Refresh();
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
        _channels.Clear(_channelId);
    }

    private void OnChannelChanged(object? sender, OutputChannelChangedEventArgs e)
    {
        if (!string.Equals(e.ChannelId, _channelId, StringComparison.Ordinal))
            return;

        Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        var entries = _channels.GetEntries(_channelId);
        Text = string.Concat(entries.Select(entry => entry.Text));
    }

    public void Dispose()
    {
        _channels.ChannelChanged -= OnChannelChanged;
    }
}
