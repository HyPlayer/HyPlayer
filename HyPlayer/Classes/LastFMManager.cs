using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using System;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyPlayer.Classes
{
    internal static class LastFMManager
    {
        public static string LastFMUserName => ApplicationData.Current.LocalSettings.Values.ContainsKey("lastFMUserName") ? ApplicationData.Current.LocalSettings.Values["lastFMUserName"].ToString() : null;
        public static string LastFMToken => ApplicationData.Current.LocalSettings.Values.ContainsKey("lastFMToken") ? ApplicationData.Current.LocalSettings.Values["lastFMToken"].ToString() : null;
        public static bool LastFMIsSubscriber => (bool)ApplicationData.Current.LocalSettings.Values["lastFMisSubscriber"];
        public static bool LastFMLogined => LastfmClient.Auth.Authenticated;
        public static LastfmClient LastfmClient = new LastfmClient("641ef15109503085d966e37b73bdcb72", "35c02c12c9c0fdc6f6c1de5d0a9227b5");
        public static async Task<LastResponse> TryLoginLastfmAccountFromInternet(string userName, string password)
        {
            var response = await LastfmClient.Auth.GetSessionTokenAsync(userName,password);
            if (response.Success)
            {
                ApplicationData.Current.LocalSettings.Values["lastFMUserName"] = LastfmClient.Auth.UserSession.Username;
                ApplicationData.Current.LocalSettings.Values["lastFMToken"] = LastfmClient.Auth.UserSession.Token;
                ApplicationData.Current.LocalSettings.Values["lastFMisSubscriber"] = LastfmClient.Auth.UserSession.IsSubscriber;
            }
            return response;
        }
        public static bool TryLoginLastfmAccountFromSession()
        {
            if (string.IsNullOrEmpty(LastFMUserName) || string.IsNullOrEmpty(LastFMToken)) return false;
            var lastUserSession = LastFMUtils.PhraseLastUserSession(LastFMToken, LastFMUserName, LastFMIsSubscriber);
            var LastfmSessionStatus = LastfmClient.Auth.LoadSession(lastUserSession);
            if (!LastfmSessionStatus)
            {
                Common.AddToTeachingTipLists("Last.FM登录过期", "Last.FM登录过期，请重新登录");
                ApplicationData.Current.LocalSettings.Values["lastFMUserName"] = null;
                ApplicationData.Current.LocalSettings.Values["lastFMToken"] = null;
                ApplicationData.Current.LocalSettings.Values["lastFMisSubscriber"] = null;
            }
            else Common.AddToTeachingTipLists("LastFM Logined");
            return LastfmSessionStatus;
        }
        public static bool TryLogoffLastFM()
        {
            ApplicationData.Current.LocalSettings.Values["lastFMUserName"] = null;
            ApplicationData.Current.LocalSettings.Values["lastFMToken"] = null;
            ApplicationData.Current.LocalSettings.Values["lastFMisSubscriber"] = null;
            LastfmClient = null;
            LastfmClient = new LastfmClient("641ef15109503085d966e37b73bdcb72", "35c02c12c9c0fdc6f6c1de5d0a9227b5");
            return true;
        }
        public static async Task<bool> ScrobbleAsync(HyPlayItem scrobbleHyPlayItem)
        {
            if (LastFMLogined == false) return false;
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
            if (LastFMLogined == false) return false;
            var nowPlayingItem = LastFMUtils.GetScrobble(nowPlayingHyPlayItem);
            var response = await LastfmClient.Track.UpdateNowPlayingAsync(nowPlayingItem);
            return response.Success;
        }
    }
    public static class LastFMUtils
    {
        public static Scrobble GetScrobble(HyPlayItem scrobbleHyPlayItem) => new Scrobble(scrobbleHyPlayItem.PlayItem.ArtistString, scrobbleHyPlayItem.PlayItem.AlbumString, scrobbleHyPlayItem.PlayItem.Name, DateTimeOffset.UtcNow);
        public static LastUserSession PhraseLastUserSession(string token, string userName, bool isSubscriber) => new LastUserSession { Token = token, Username = userName, IsSubscriber = isSubscriber };
    }
}
