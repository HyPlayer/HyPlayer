using System.Collections.Generic;
using System.Threading.Tasks;
using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.Playback.Services;

public interface ISongListQueueBuilder
{
    Task BuildAndPlayAsync(SingleSongBase clickedSong, SongListQueueScope scope,
        IReadOnlyList<SingleSongBase> visibleSongs);
}