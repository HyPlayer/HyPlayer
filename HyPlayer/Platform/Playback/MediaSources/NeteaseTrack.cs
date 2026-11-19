using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Platform.Playback.MediaSources;

public static class NeteaseTrack
{
    public static SingleSongBase Of(SingleSongBase song)
    {
        return song is NeteaseRadioProgram { MainSong: { ActualId.Length: > 0 } mainSong } ? mainSong : song;
    }

    public static string? IdOf(SingleSongBase? song)
    {
        return song is null ? null : Of(song).ActualId;
    }
}
