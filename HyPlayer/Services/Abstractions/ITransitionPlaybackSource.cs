using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Threading.Tasks;

namespace HyPlayer.Services.Abstractions;

public interface ITransitionPlaybackSource : IAsyncDisposable
{
    SingleSongBase Item { get; }

    double SuggestedVolume { get; }

    Task PlayAsync();

    Task SetVolumeAsync(double volume);

    Task SetAsPrimaryAsync();

    Task DisconnectAsync();
}
