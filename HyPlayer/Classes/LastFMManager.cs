using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Windows.System;
using Windows.Web;
using System.Xml;
using Newtonsoft.Json.Linq;
using HyPlayer.HyPlayControl;

namespace HyPlayer.Classes
{
    internal static class LastFMManager
    {
        public delegate void LoginDoneEvent();
        public delegate void LogoffDoneEvent();
        public static event LoginDoneEvent OnLoginDone;
        public static event LogoffDoneEvent OnLogoffDone;
        public static string LastFMAPIKey = "641ef15109503085d966e37b73bdcb72";
        public static string LastFMAPISecret = "35c02c12c9c0fdc6f6c1de5d0a9227b5";
        public static bool LastfmLogined => LastfmClient.Auth.Authenticated;
        private static LastfmClient LastfmClient = new LastfmClient(LastFMAPIKey, LastFMAPISecret);
        public static void InitializeLastFMManager()
        {
            OnLoginDone += LastFMManager_OnLoginDone;
            OnLogoffDone += LastFMManager_OnLogoffDone;
            HyPlayList.OnPlayItemChange += HyPlayList.UpdateLastFMNowPlayingAsync;
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
            LastUserSession session = new LastUserSession
            {
                Username = Common.Setting.LastFMUserName,
                Token = Common.Setting.LastFMToken,
                IsSubscriber = Common.Setting.LastFMIsSubscriber
            };
            var LastfmSessionStatus = LastfmClient.Auth.LoadSession(session);
            if (!LastfmSessionStatus)
            {
                Common.AddToTeachingTipLists("Last.FM登录过期", "Last.FM登录过期，请重新登录");
                OnLogoffDone.Invoke();
            }
            return LastfmSessionStatus;
        }
        public static async void TryLoginLastfmAccountFromBrowser(string token="")
        {
            var signature = LastFMUtils.GetLastFMAPISignature(token);
            HttpClient httpClient = new HttpClient();
            string result = string.Empty;
            result = await httpClient.GetStringAsync("https://ws.audioscrobbler.com/2.0/?method=auth.getSession&format=json&token=" + token + "&api_key=" + LastFMAPIKey + "&api_sig=" + signature );
            httpClient.Dispose();
            JObject sessionJsonData = JObject.Parse(result);
            LastUserSession session = new LastUserSession
            {
                Username = sessionJsonData["session"]["name"].ToString(),
                Token = sessionJsonData["session"]["key"].ToString(),
                IsSubscriber = sessionJsonData["session"]["subscriber"].ToString() == "1",
            };
            LastfmClient.Auth.LoadSession(session);
            OnLoginDone.Invoke();
        }
        public static bool TryLogoffLastFM()
        {
            LastfmClient = new LastfmClient("641ef15109503085d966e37b73bdcb72", "35c02c12c9c0fdc6f6c1de5d0a9227b5");
            OnLogoffDone.Invoke();
            return true;
        }
        public static async Task<bool> ScrobbleAsync(HyPlayItem scrobbleHyPlayItem)
        {
            if (Common.Setting.LastFMLogined == false || Common.Setting.UseLastFMScrobbler == false) return false;
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
            if (Common.Setting.LastFMLogined == false || Common.Setting.UpdateLastFMNowPlaying == false) return false;
            var nowPlayingItem = LastFMUtils.GetScrobble(nowPlayingHyPlayItem);
            var response = await LastfmClient.Track.UpdateNowPlayingAsync(nowPlayingItem);
            if (!response.Success) throw new Exception(response.Status.ToString());
            return response.Success;
        }
    }
    public static class LastFMUtils
    {
        public static Scrobble GetScrobble(HyPlayItem scrobbleHyPlayItem) => new Scrobble(scrobbleHyPlayItem.PlayItem.ArtistString, scrobbleHyPlayItem.PlayItem.AlbumString, scrobbleHyPlayItem.PlayItem.Name, DateTimeOffset.UtcNow);
        public static string GetLastFMAPISignature(string token)
        {

            var byteData = HyPlayer.Extensions.ToByteArrayUtf8("api_key"+LastFMManager.LastFMAPIKey + "methodauth.getSessiontoken" + token + LastFMManager.LastFMAPISecret);
            var md5 = HyPlayer.Extensions.ComputeMd5(byteData);
            StringBuilder stringBuilder = new StringBuilder();
            foreach (var data in md5)
            {
                stringBuilder.Append(data.ToString("x2"));
            }
            return stringBuilder.ToString();
        }
    }
}
