using HyPlayer.Classes;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
#nullable enable

namespace HyPlayer.Contracts.Services
{
    public interface INeteaseProviderService
    {
        bool IsLoggedIn { get; }

        /// <summary> Get Recommended Resource. </summary>
        Task<List<ProvidableItemBase>> GetRecommendedResourceAsync(string? typeId = null, CancellationToken token = new());

        /// <summary> Get Album Details. </summary>
        Task<(NCAlbum? album, List<NCSong>? songs)> GetAlbumDetailsAsync(string albumId, CancellationToken token = new());

        /// <summary> Get Playlist Details. </summary>
        Task<(NCPlayList? playlist, List<NCSong>? songs)> GetPlaylistDetailsAsync(string playlistId, CancellationToken token = new());

        /// <summary> Get Artist Details. </summary>
        Task<NCArtist?> GetArtistDetailsAsync(string artistId, CancellationToken token = new());

        /// <summary> Get Artist Hot Songs. </summary>
        Task<List<NCSong>?> GetArtistHotSongsAsync(string artistId, CancellationToken token = new());

        /// <summary> Get Artist Albums. </summary>
        Task<List<NCAlbum>?> GetArtistAlbumsAsync(string artistId, int limit = 50, CancellationToken token = new());
    }
}
