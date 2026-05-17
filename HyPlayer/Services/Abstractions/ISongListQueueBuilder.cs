using HyPlayer.Classes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HyPlayer.Services.Abstractions;

public interface ISongListQueueBuilder
{
    Task BuildAndPlayAsync(NCSong clickedSong, SongListQueueScope scope, IReadOnlyList<NCSong> visibleSongs);
}
