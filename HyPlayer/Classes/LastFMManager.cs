using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using IF.Lastfm.Core.Scrobblers;
using System;
using System.Threading.Tasks;

namespace HyPlayer.Classes
{
    internal static class LastFMManager
    {
        public static LastfmClient LastfmClient = new LastfmClient("641ef15109503085d966e37b73bdcb72", "35c02c12c9c0fdc6f6c1de5d0a9227b5");
        public static IScrobbler Scrobbler;
        public static async Task<LastResponse> TryLoginLastfmAccountFromInternet(string userName, string password)
        {
            var response = await LastfmClient.Auth.GetSessionTokenAsync(userName,password);
            return response;
        }
        public static bool TryLoginLastfmAccountFromSession(LastUserSession cachedSession)
        {
            var LastfmSessionStatus = LastfmClient.Auth.LoadSession(cachedSession);
            if (!LastfmSessionStatus)
            {
                throw new Exception(LastResponseStatus.BadAuth.ToString());
            }
            return LastfmSessionStatus;
        }
        public static async Task<bool> ScrobbleAsync(HyPlayItem scrobbleHyPlayItem)
        {
            var scrobbleItem = LastFMUtils.GetScrobble(scrobbleHyPlayItem);
            var response= await LastfmClient.Scrobbler.ScrobbleAsync(scrobbleItem);
            if (!response.Success)
            {
                throw response.Exception;
            }
            return response.Success;
        }
        public static async Task<bool> UpdateNowPlayingAsync(HyPlayItem nowPlayingHyPlayItem)
        {
            var nowPlayingItem = LastFMUtils.GetScrobble(nowPlayingHyPlayItem);
            var response = await LastfmClient.Track.UpdateNowPlayingAsync(nowPlayingItem);
            return response.Success;
        }
    }
    public static class LastFMUtils
    {
        public static Scrobble GetScrobble(HyPlayItem scrobbleHyPlayItem) => new Scrobble(scrobbleHyPlayItem.PlayItem.ArtistString, scrobbleHyPlayItem.PlayItem.AlbumString, scrobbleHyPlayItem.PlayItem.Name, DateTimeOffset.UtcNow);
    }
}
