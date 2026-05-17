using System;
using System.Threading.Tasks;
using HyPlayer.Classes;

namespace HyPlayer.Services.Abstractions;

public interface IAppNavigator
{
    Task AppendAsync(MusicResource resource);

    Task NavigateAsync(AppRoute route);

    AppRoute? InferRoute(Type pageType, object? parameter);

    Task PlayAsync(MusicResource resource);

    Task PlaySongAsync(string songId);

    void SetPlaybackSource(MusicResource resource);
}
