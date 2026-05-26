using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.PlayCoreBridge;

public sealed class TransitionPlaybackSource : ITransitionPlaybackSource
{
    private readonly ChopinAudioServiceAdapter _adapter;
    private readonly AudioGraphPlayer _player;
    private readonly ChopinAudioTicket _ticket;
    private bool _disconnected;

    public TransitionPlaybackSource(
        SingleSongBase item,
        ChopinAudioServiceAdapter adapter,
        AudioGraphPlayer player,
        ChopinAudioTicket ticket)
    {
        Item = item;
        _adapter = adapter;
        _player = player;
        _ticket = ticket;
        SuggestedVolume = ticket.Volume;
    }

    public SingleSongBase Item { get; }

    public double SuggestedVolume { get; }

    public Task PlayAsync() => _adapter.PlayAudioTicketAsync(_ticket);

    public Task SetVolumeAsync(double volume) => _adapter.ChangeVolumeAsync(_ticket, volume);

    public Task SetAsPrimaryAsync()
    {
        _player.PrimaryPlaybackSource = _ticket.PlaybackSource;
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        if (_disconnected)
            return;

        _disconnected = true;
        await _adapter.DisposeAudioTicketAsync(_ticket);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}
