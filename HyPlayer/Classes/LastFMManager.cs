using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.UI.Core;

namespace HyPlayer.Classes
{
    internal static class LastFMManager
    {
        public delegate void LoginDoneEvent();
        public delegate void LogoffDoneEvent();
        public static event LoginDoneEvent OnLoginDone;
        public static event LogoffDoneEvent OnLogoffDone;
        public static LastfmClient LastfmClient = new LastfmClient("641ef15109503085d966e37b73bdcb72", "35c02c12c9c0fdc6f6c1de5d0a9227b5");
        public static void InitializeLastFMManager()
        {
            OnLoginDone += LastFMManager_OnLoginDone;
            OnLogoffDone += LastFMManager_OnLogoffDone;
            TryLoginLastfmAccountFromSession();
        }

        private static void LastFMManager_OnLogoffDone()
        {
            Common.Setting.LastFMUserName = null;
            Common.Setting.LastFMToken = null;
            Common.Setting.LastFMIsSubscriber = false;
        }

        private static void LastFMManager_OnLoginDone()
        {
            Common.Setting.LastFMUserName = LastfmClient.Auth.UserSession.Username;
            Common.Setting.LastFMToken = LastfmClient.Auth.UserSession.Token;
            Common.Setting.LastFMIsSubscriber = LastfmClient.Auth.UserSession.IsSubscriber;
        }

        public static async Task<LastResponse> TryLoginLastfmAccountFromInternet(string userName, string password)
        {
            if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password)) throw new Exception("用户名或密码不能为空");
            var response = await LastfmClient.Auth.GetSessionTokenAsync(userName,password);
            if (response.Success) OnLoginDone.Invoke();
            else
            {
                OnLogoffDone.Invoke();
                throw new Exception(response.Status.ToString());
            }
            return response;
        }
        public static bool TryLoginLastfmAccountFromSession()
        {
            if (string.IsNullOrEmpty(Common.Setting.LastFMUserName) || string.IsNullOrEmpty(Common.Setting.LastFMToken)) return false;
            var lastUserSession = LastFMUtils.PhraseLastUserSession(Common.Setting.LastFMToken, Common.Setting.LastFMUserName, Common.Setting.LastFMIsSubscriber);
            var LastfmSessionStatus = LastfmClient.Auth.LoadSession(lastUserSession);
            if (!LastfmSessionStatus)
            {
                Common.AddToTeachingTipLists("Last.FM登录过期", "Last.FM登录过期，请重新登录");
                OnLogoffDone.Invoke();
            }
            else Common.AddToTeachingTipLists("LastFM Logined");
            return LastfmSessionStatus;
        }
        public static bool TryLogoffLastFM()
        {
            LastfmClient = new LastfmClient("641ef15109503085d966e37b73bdcb72", "35c02c12c9c0fdc6f6c1de5d0a9227b5");
            OnLogoffDone.Invoke();
            return true;
        }
        public static async Task<bool> ScrobbleAsync(HyPlayItem scrobbleHyPlayItem)
        {
            if (Common.Setting.LastFMLogined == false) return false;
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
            if (Common.Setting.LastFMLogined == false) return false;
            var nowPlayingItem = LastFMUtils.GetScrobble(nowPlayingHyPlayItem);
            var response = await LastfmClient.Track.UpdateNowPlayingAsync(nowPlayingItem);
            if (!response.Success) throw new Exception(response.Status.ToString());
            return response.Success;
        }
    }
    public static class LastFMUtils
    {
        public static Scrobble GetScrobble(HyPlayItem scrobbleHyPlayItem) => new Scrobble(scrobbleHyPlayItem.PlayItem.ArtistString, scrobbleHyPlayItem.PlayItem.AlbumString, scrobbleHyPlayItem.PlayItem.Name, DateTimeOffset.UtcNow);
        public static LastUserSession PhraseLastUserSession(string token, string userName, bool isSubscriber) => new LastUserSession { Token = token, Username = userName, IsSubscriber = isSubscriber };
    }
}
